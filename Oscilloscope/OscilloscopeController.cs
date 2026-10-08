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
    [SerializeField] public TMP_InputField yScaleField2;
    [SerializeField] public Scrollbar timeDivScrollbar;
    [SerializeField] public TMP_Text timeDivText;

    [Header("Local trigger controls")]
    [SerializeField] public Scrollbar triggerEnableScrollbar;
    [SerializeField] public Scrollbar triggerModeScrollbar;
    [SerializeField] public TMP_Dropdown triggerSourceDropdown;
    [SerializeField] public Scrollbar triggerEdgeScrollbar;
    [SerializeField] public TMP_InputField triggerLevelField;
    [SerializeField] public TMP_Text triggerEnableText;
    [SerializeField] public TMP_Text triggerModeText;
    [SerializeField] public TMP_Text triggerEdgeText;

    [Header("GPU acquisition")]
    [SerializeField] public RenderTexture bufferTemplate;
    [SerializeField] public Material triggerStateMaterial;
    [SerializeField] public Material triggerPreMaterial;
    [SerializeField] public Material triggerCaptureMaterial;
    [SerializeField] public Material copyMaterial;
    [SerializeField] public Material ch1Material;
    [SerializeField] public Material ch2Material;

    private const float VerticalDivisionCount = 8f;
    private const int TriggerOff = 0;
    private const int TriggerSingle = 0;
    private const int TriggerNormal = 1;
    private const int TriggerAuto = 2;

    private RenderTexture state0;
    private RenderTexture state1;
    private RenderTexture pre0;
    private RenderTexture pre1;
    private RenderTexture capture0;
    private RenderTexture capture1;
    private RenderTexture display0;
    private RenderTexture display1;
    private bool buffers0AreCurrent;

    private int stride = 10;
    private int samplingRevision = 1;
    private int triggerRevision = 1;
    private int row1 = int.MinValue;
    private int row2 = int.MinValue;
    private string lastProbe1 = "";
    private string lastProbe2 = "";
    private string pendingProbe1 = "";
    private string pendingProbe2 = "";
    private int stableProbeFrames;
    private float yDiv1 = float.NaN;
    private float yDiv2 = float.NaN;
    private bool firstCopy = true;

    private bool triggerEnabled;
    private int triggerMode;
    private int triggerSource;
    private bool triggerFalling;
    private float triggerLevel;

    private void Start()
    {
        if (bufferTemplate != null)
        {
            state0 = CreateBuffer(32, 1);
            state1 = CreateBuffer(32, 1);
            pre0 = CreateBuffer(4, 128);
            pre1 = CreateBuffer(4, 128);
            capture0 = CreateBuffer(4, 256);
            capture1 = CreateBuffer(4, 256);
            display0 = CreateBuffer(4, 256);
            display1 = CreateBuffer(4, 256);
        }
        if (timeDivScrollbar != null)
        {
            timeDivScrollbar.numberOfSteps = 5;
            timeDivScrollbar.value = 0.5f;
        }
        InitializeTriggerControls();
        ChangeTimeDivision();
        RefreshProbeRows(true);
        RefreshVoltageScale(true);
        RefreshTriggerLabels();
    }

    private RenderTexture CreateBuffer(int width, int height)
    {
        RenderTexture buffer = new RenderTexture(bufferTemplate);
        buffer.width = width;
        buffer.height = height;
        buffer.filterMode = FilterMode.Point;
        buffer.wrapMode = TextureWrapMode.Clamp;
        buffer.useMipMap = false;
        buffer.autoGenerateMips = false;
        buffer.antiAliasing = 1;
        buffer.Create();
        return buffer;
    }

    private void InitializeTriggerControls()
    {
        triggerEnabled = false;
        triggerMode = TriggerSingle;
        triggerSource = 0;
        triggerFalling = false;
        triggerLevel = 0f;
        if (triggerEnableScrollbar != null)
        {
            triggerEnableScrollbar.numberOfSteps = 2;
            triggerEnableScrollbar.SetValueWithoutNotify(0f);
        }
        if (triggerModeScrollbar != null)
        {
            triggerModeScrollbar.numberOfSteps = 3;
            triggerModeScrollbar.SetValueWithoutNotify(0f);
        }
        if (triggerSourceDropdown != null) triggerSourceDropdown.SetValueWithoutNotify(0);
        if (triggerEdgeScrollbar != null)
        {
            triggerEdgeScrollbar.numberOfSteps = 2;
            triggerEdgeScrollbar.SetValueWithoutNotify(0f);
        }
        if (triggerLevelField != null) triggerLevelField.SetTextWithoutNotify("0");
    }

    public void ChangeTimeDivision()
    {
        int index = timeDivScrollbar == null ? 2 : Mathf.RoundToInt(timeDivScrollbar.value * 4f);
        if (index <= 0) stride = 1;
        else if (index == 1) stride = 5;
        else if (index == 2) stride = 10;
        else if (index == 3) stride = 50;
        else stride = 100;
        AdvanceSamplingConfiguration();
        RefreshTimeLabel();
    }

    public void ChangeTriggerSettings()
    {
        bool nextEnabled = triggerEnableScrollbar != null && triggerEnableScrollbar.value >= 0.5f;
        int nextMode = triggerModeScrollbar == null ? TriggerSingle :
            Mathf.Clamp(Mathf.RoundToInt(triggerModeScrollbar.value * 2f), TriggerSingle, TriggerAuto);
        int nextSource = triggerSourceDropdown == null ? 0 : Mathf.Clamp(triggerSourceDropdown.value, 0, 1);
        bool nextFalling = triggerEdgeScrollbar != null && triggerEdgeScrollbar.value >= 0.5f;
        float nextLevel = triggerLevel;
        bool levelValid = true;
        if (triggerLevelField != null)
        {
            float parsed;
            levelValid = float.TryParse(triggerLevelField.text, out parsed) &&
                !float.IsNaN(parsed) && !float.IsInfinity(parsed);
            if (levelValid) nextLevel = parsed;
            else triggerLevelField.SetTextWithoutNotify(triggerLevel.ToString("G9"));
        }

        bool changed = nextEnabled != triggerEnabled || nextMode != triggerMode ||
            nextSource != triggerSource || nextFalling != triggerFalling ||
            (levelValid && nextLevel != triggerLevel);
        triggerEnabled = nextEnabled;
        triggerMode = nextMode;
        triggerSource = nextSource;
        triggerFalling = nextFalling;
        if (levelValid) triggerLevel = nextLevel;
        if (changed) triggerRevision = NextRevision(triggerRevision);
        RefreshTriggerLabels();
    }

    private void RefreshTriggerLabels()
    {
        if (triggerEnableText != null)
        {
            triggerEnableText.text = triggerEnabled ? "ON" : "OFF";
            triggerEnableText.color = triggerEnabled ? new Color(0.15f, 0.9f, 0.3f, 1f) :
                new Color(0.65f, 0.65f, 0.65f, 1f);
        }
        if (triggerModeText != null)
        {
            triggerModeText.text = triggerMode == TriggerNormal ? "NORMAL" :
                (triggerMode == TriggerAuto ? "AUTO" : "SINGLE");
            triggerModeText.color = triggerMode == TriggerNormal ? new Color(0.15f, 0.9f, 0.3f, 1f) :
                (triggerMode == TriggerAuto ? new Color(1f, 0.6f, 0.15f, 1f) :
                    new Color(0.15f, 0.7f, 1f, 1f));
        }
        if (triggerEdgeText != null)
        {
            triggerEdgeText.text = triggerFalling ? "FALL" : "RISE";
            triggerEdgeText.color = triggerFalling ? new Color(1f, 0.6f, 0.15f, 1f) :
                new Color(0.15f, 0.7f, 1f, 1f);
        }
    }

    private int NextRevision(int value) { return value >= int.MaxValue ? 1 : value + 1; }
    private void AdvanceSamplingConfiguration() { samplingRevision = NextRevision(samplingRevision); }

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

        int next1 = ResolveRow(probe1);
        int next2 = ResolveRow(probe2);
        bool selectionChanged = probe1 != lastProbe1 || probe2 != lastProbe2;
        bool rowChanged = next1 != row1 || next2 != row2;
        if (!selectionChanged && !rowChanged) return;
        lastProbe1 = probe1;
        lastProbe2 = probe2;
        row1 = next1;
        row2 = next2;
        AdvanceSamplingConfiguration();
    }

    public void ChangeVoltageScale()
    {
        RefreshVoltageScale(true);
    }

    private float ReadVoltageScale(TMP_InputField field, float current, bool commit)
    {
        float fallback = current > 0f && !float.IsNaN(current) && !float.IsInfinity(current) ? current : 1f;
        if (field == null) return fallback;
        float parsed;
        bool valid = float.TryParse(field.text, out parsed) && parsed > 0f &&
            !float.IsNaN(parsed) && !float.IsInfinity(parsed);
        if (valid) return parsed;
        if (commit) field.SetTextWithoutNotify(fallback.ToString("G9"));
        return fallback;
    }

    private void RefreshVoltageScale(bool commit)
    {
        float next1 = ReadVoltageScale(yScaleField, yDiv1, commit);
        float next2 = yScaleField2 == null ? next1 : ReadVoltageScale(yScaleField2, yDiv2, commit);
        if (next1 != yDiv1)
        {
            yDiv1 = next1;
            if (ch1Material != null)
                ch1Material.SetFloat("_YScale", 1f / (VerticalDivisionCount * yDiv1));
        }
        if (next2 != yDiv2)
        {
            yDiv2 = next2;
            if (ch2Material != null)
                ch2Material.SetFloat("_YScale", 1f / (VerticalDivisionCount * yDiv2));
        }
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

    private void SetCommonMaterialParameters(Material material)
    {
        if (material == null) return;
        solver.WriteOutputDumpToMaterial(material);
        material.SetInteger("_Row1", row1);
        material.SetInteger("_Row2", row2);
        material.SetInteger("_Stride", stride);
        material.SetInteger("_SamplingRevision", samplingRevision);
        material.SetInteger("_TriggerRevision", triggerRevision);
        material.SetInteger("_TriggerEnabled", triggerEnabled ? 1 : TriggerOff);
        material.SetInteger("_TriggerMode", triggerMode);
        material.SetInteger("_TriggerSource", triggerSource);
        material.SetInteger("_TriggerFalling", triggerFalling ? 1 : 0);
        material.SetFloat("_TriggerLevel", triggerLevel);
        material.SetInteger("_ForceReset", firstCopy ? 1 : 0);
    }

    private void LateUpdate()
    {
        RefreshProbeRows(false);
        RefreshVoltageScale(false);
        RefreshTimeLabel();
        if (solver == null || triggerStateMaterial == null || triggerPreMaterial == null ||
            triggerCaptureMaterial == null || copyMaterial == null || state0 == null || state1 == null ||
            pre0 == null || pre1 == null || capture0 == null || capture1 == null ||
            display0 == null || display1 == null || !solver.HasOutputDump()) return;

        RenderTexture stateSource = buffers0AreCurrent ? state0 : state1;
        RenderTexture stateDestination = buffers0AreCurrent ? state1 : state0;
        RenderTexture preSource = buffers0AreCurrent ? pre0 : pre1;
        RenderTexture preDestination = buffers0AreCurrent ? pre1 : pre0;
        RenderTexture captureSource = buffers0AreCurrent ? capture0 : capture1;
        RenderTexture captureDestination = buffers0AreCurrent ? capture1 : capture0;
        RenderTexture displaySource = buffers0AreCurrent ? display0 : display1;
        RenderTexture displayDestination = buffers0AreCurrent ? display1 : display0;

        SetCommonMaterialParameters(triggerStateMaterial);
        triggerStateMaterial.SetTexture("_PreDump", preSource);
        VRCGraphics.Blit(stateSource, stateDestination, triggerStateMaterial);

        SetCommonMaterialParameters(triggerPreMaterial);
        triggerPreMaterial.SetTexture("_StateDump", stateDestination);
        triggerPreMaterial.SetTexture("_PreviousState", stateSource);
        VRCGraphics.Blit(preSource, preDestination, triggerPreMaterial);

        SetCommonMaterialParameters(triggerCaptureMaterial);
        triggerCaptureMaterial.SetTexture("_StateDump", stateDestination);
        triggerCaptureMaterial.SetTexture("_PreviousState", stateSource);
        triggerCaptureMaterial.SetTexture("_PreDump", preSource);
        VRCGraphics.Blit(captureSource, captureDestination, triggerCaptureMaterial);

        SetCommonMaterialParameters(copyMaterial);
        copyMaterial.SetTexture("_StateDump", stateDestination);
        copyMaterial.SetTexture("_CaptureDump", captureDestination);
        VRCGraphics.Blit(displaySource, displayDestination, copyMaterial);

        buffers0AreCurrent = !buffers0AreCurrent;
        firstCopy = false;
        RenderTexture currentDisplay = buffers0AreCurrent ? display0 : display1;
        if (ch1Material != null) ch1Material.SetTexture("_DisplayDump", currentDisplay);
        if (ch2Material != null) ch2Material.SetTexture("_DisplayDump", currentDisplay);
    }

    private void DestroyBuffer(RenderTexture buffer)
    {
        if (buffer != null) Destroy(buffer);
    }

    private void OnDestroy()
    {
        DestroyBuffer(state0); DestroyBuffer(state1);
        DestroyBuffer(pre0); DestroyBuffer(pre1);
        DestroyBuffer(capture0); DestroyBuffer(capture1);
        DestroyBuffer(display0); DestroyBuffer(display1);
    }
}
