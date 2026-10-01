using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;

public class Test : UdonSharpBehaviour
{
    [SerializeField] private TMP_InputField netlistfield;
    [SerializeField] private TMP_InputField probefield1;
    [SerializeField] private TMP_InputField probefield2;
    [SerializeField] private TMP_InputField yscalefield;
    [SerializeField] private TMP_InputField xscalefield;
    [SerializeField] private TMP_InputField stepsPerFrame;
    [SerializeField] private TMP_InputField maxError; // Retained for scene compatibility; no longer used.
    private MNAGen generator;
    private MNASolve solver;
    private int oscind1 = int.MinValue;
    private int oscind2 = int.MinValue;

    private void Start()
    {
        solver = GetComponent<MNASolve>();
        generator = GetComponent<MNAGen>();
    }

    private void Update()
    {
        if (solver == null) return;
        int next1 = probefield1 != null && probefield1.text == "GND" ? -2 :
            (probefield1 == null ? -1 : solver.label2bufferRow(probefield1.text));
        int next2 = probefield2 != null && probefield2.text == "GND" ? -2 :
            (probefield2 == null ? -1 : solver.label2bufferRow(probefield2.text));
        Renderer target = GetComponent<Renderer>();
        if (target == null) return;
        if (oscind1 != next1) { oscind1 = next1; target.material.SetInteger("_Row1", oscind1); }
        if (oscind2 != next2) { oscind2 = next2; target.material.SetInteger("_Row2", oscind2); }

        float ydiv = 1f, xdiv = 1f;
        if (yscalefield != null) float.TryParse(yscalefield.text, out ydiv);
        if (xscalefield != null) float.TryParse(xscalefield.text, out xdiv);
        solver.WriteToMaterial(target.material);
        if (ydiv > 0f) target.material.SetFloat("_YScale", 0.1f / ydiv);
        if (xdiv > 0f) target.material.SetFloat("_XScale", 0.1f / xdiv);

        int steps = 100;
        if (stepsPerFrame != null) int.TryParse(stepsPerFrame.text, out steps);
        if (steps > 0) solver.SetStepPerFrame(steps);
    }

    public void restart()
    {
        if (netlistfield == null || generator == null) return;
        string[] lines = netlistfield.text.Split('\n');
        DataList nextNetlist = new DataList();
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) continue;
            string[] elements = lines[i].Split(' ');
            int netCount = 0, constantCount = 0;
            string prefix = elements[0].Substring(0, 1);
            if (prefix == "R" || prefix == "C" || prefix == "V" || prefix == "L" || prefix == "I")
            { netCount = 2; constantCount = 1; }
            else if (prefix == "D")
            {
                netCount = 2; constantCount = 7;
                if (elements.Length != 10) { Debug.LogError(elements[0] + ": expected Dname anode cathode Is Vt TT Cjo Vj m Fc."); return; }
            }
            else if (prefix == "Q") { netCount = 3; constantCount = 0; }
            if (elements.Length < 1 + netCount + constantCount || netCount == 0) continue;
            DataList component = new DataList(); DataList nets = new DataList(); DataList constants = new DataList();
            for (int n = 0; n < netCount; n++) nets.Add(elements[1 + n]);
            for (int c = 0; c < constantCount; c++)
            {
                float value;
                if (!float.TryParse(elements[1 + netCount + c], out value))
                { Debug.LogError(elements[0] + ": invalid numeric constant."); return; }
                constants.Add(value);
            }
            component.Add(elements[0]); component.Add(nets); component.Add(constants); nextNetlist.Add(component);
        }
        generator.netlist = nextNetlist;
        generator.UpdateMNA();
    }
}
