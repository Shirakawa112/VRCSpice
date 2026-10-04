using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Rendering;
using VRC.SDKBase;

public class OscilloscopeController : UdonSharpBehaviour
{
    [SerializeField] public MNASolve solver;
    [SerializeField] public TMP_InputField probeField1;
    [SerializeField] public TMP_InputField probeField2;
    [SerializeField] public TMP_InputField yScaleField;
    [SerializeField] public Scrollbar timeDivScrollbar;
    [SerializeField] public TMP_Text timeDivText;
    [SerializeField] public RenderTexture bufferTemplate;
    [SerializeField] public Material copyMaterial;
    [SerializeField] public Material ch1Material;
    [SerializeField] public Material ch2Material;

    private const float VerticalDivisionCount = 8f;
    private RenderTexture display0;
    private RenderTexture display1;
    private bool display0IsCurrent;
    private int stride = 10;
    private int configRevision = 1;
    private int row1 = int.MinValue;
    private int row2 = int.MinValue;
    private string lastProbe1 = "";
    private string lastProbe2 = "";
    private string pendingProbe1 = "";
    private string pendingProbe2 = "";
    private int stableProbeFrames;
    private float yDiv = float.NaN;
    private bool firstCopy = true;

    private void Start()
    {
        if (bufferTemplate != null)
        {
            display0 = CreateDisplayBuffer();
            display1 = CreateDisplayBuffer();
        }
        if (timeDivScrollbar != null)
        {
            timeDivScrollbar.numberOfSteps = 5;
            timeDivScrollbar.value = 0.5f;
        }
        ChangeTimeDivision();
        RefreshProbeRows(true);
        RefreshVoltageScale();
    }

    private RenderTexture CreateDisplayBuffer()
    {
        RenderTexture buffer = new RenderTexture(bufferTemplate);
        buffer.width = 4;
        buffer.height = 256;
        buffer.filterMode = FilterMode.Point;
        buffer.wrapMode = TextureWrapMode.Clamp;
        buffer.useMipMap = false;
        buffer.autoGenerateMips = false;
        buffer.antiAliasing = 1;
        buffer.Create();
        return buffer;
    }

    public void ChangeTimeDivision()
    {
        int index = timeDivScrollbar == null ? 2 : Mathf.RoundToInt(timeDivScrollbar.value * 4f);
        if (index <= 0) stride = 1;
        else if (index == 1) stride = 5;
        else if (index == 2) stride = 10;
        else if (index == 3) stride = 50;
        else stride = 100;
        AdvanceConfiguration();
        RefreshTimeLabel();
    }

    private void AdvanceConfiguration()
    {
        configRevision = configRevision >= int.MaxValue ? 1 : configRevision + 1;
    }

    private int ResolveRow(string label)
    {
        if (label == "GND") return -2;
        if (label == null || label == "" || solver == null) return -1;
        return solver.label2bufferRow(label);
    }

    private void RefreshProbeRows(bool force)
    {
        string probe1 = probeField1 == null ? "" : probeField1.text;
        string probe2 = probeField2 == null ? "" : probeField2.text;
        if (force)
        {
            pendingProbe1 = probe1;
            pendingProbe2 = probe2;
            stableProbeFrames = 4;
        }
        else if (probe1 != pendingProbe1 || probe2 != pendingProbe2)
        {
            pendingProbe1 = probe1;
            pendingProbe2 = probe2;
            stableProbeFrames = 0;
            return;
        }
        else if (stableProbeFrames < 4)
        {
            stableProbeFrames++;
            return;
        }

        int next1 = ResolveRow(probe1); int next2 = ResolveRow(probe2);
        bool selectionChanged = probe1 != lastProbe1 || probe2 != lastProbe2;
        bool rowChanged = next1 != row1 || next2 != row2;
        if (!selectionChanged && !rowChanged) return;

        // Probe text can settle before MNAGen publishes its vector. Rebuild
        // the display when the same label later resolves to a valid MNA row.
        lastProbe1 = probe1;
        lastProbe2 = probe2;
        row1 = next1;
        row2 = next2;
        AdvanceConfiguration();
    }

    private void RefreshVoltageScale()
    {
        float next = yDiv;
        if (yScaleField != null)
        {
            float parsed;
            if (float.TryParse(yScaleField.text, out parsed) && parsed > 0f &&
                !float.IsNaN(parsed) && !float.IsInfinity(parsed)) next = parsed;
        }
        if (next == yDiv && ch1Material != null && ch2Material != null) return;
        yDiv = next;
        float scale = 1f / (VerticalDivisionCount * Mathf.Max(0.000000001f, yDiv));
        if (ch1Material != null) ch1Material.SetFloat("_YScale", scale);
        if (ch2Material != null) ch2Material.SetFloat("_YScale", scale);
    }

    private string FormatTime(float seconds)
    {
        if (seconds >= 1f) return seconds.ToString("0.###") + " s";
        if (seconds >= 0.001f) return (seconds * 1000f).ToString("0.###") + " ms";
        if (seconds >= 0.000001f) return (seconds * 1000000f).ToString("0.###") + " us";
        return (seconds * 1000000000f).ToString("0.###") + " ns";
    }

    private void RefreshTimeLabel()
    {
        if (timeDivText == null) return;
        float dt = solver == null ? 0f : solver.GetOutputDeltaTime();
        timeDivText.text = FormatTime(dt * stride * 20f) + "/div  x" + stride;
    }

    private void LateUpdate()
    {
        RefreshProbeRows(false);
        RefreshVoltageScale();
        RefreshTimeLabel();
        if (solver == null || copyMaterial == null || display0 == null || display1 == null ||
            !solver.HasOutputDump()) return;

        solver.WriteOutputDumpToMaterial(copyMaterial);
        copyMaterial.SetInteger("_Row1", row1);
        copyMaterial.SetInteger("_Row2", row2);
        copyMaterial.SetInteger("_Stride", stride);
        copyMaterial.SetInteger("_ConfigRevision", configRevision);
        copyMaterial.SetInteger("_ForceRebuild", firstCopy ? 1 : 0);

        RenderTexture source = display0IsCurrent ? display0 : display1;
        RenderTexture destination = display0IsCurrent ? display1 : display0;
        VRCGraphics.Blit(source, destination, copyMaterial);
        display0IsCurrent = !display0IsCurrent;
        firstCopy = false;
        RenderTexture current = display0IsCurrent ? display0 : display1;
        if (ch1Material != null) ch1Material.SetTexture("_DisplayDump", current);
        if (ch2Material != null) ch2Material.SetTexture("_DisplayDump", current);
    }

    private void OnDestroy()
    {
        if (display0 != null) Destroy(display0);
        if (display1 != null) Destroy(display1);
    }
}
