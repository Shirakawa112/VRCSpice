using System.IO;
using TMPro;
using TMPro.EditorUtilities;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public static class OscilloscopeDumpBuilder
{
    private const string ScenePath = "Assets/VRCSpice/Scene.unity";
    private const string ControllerScript = "Assets/VRCSpice/Oscilloscope/OscilloscopeController.cs";
    private const string ControllerAsset = "Assets/VRCSpice/Oscilloscope/OscilloscopeController.asset";
    private const string ExtractMaterialPath = "Assets/VRCSpice/MNATools/Shader/SolverOutputDump.mat";
    private const string CopyMaterialPath = "Assets/VRCSpice/Oscilloscope/OscilloscopeCopy.mat";
    private const string PointMeshPath = "Assets/VRCSpice/Oscilloscope/OscilloscopePoint.asset";

    private static void Check(bool condition,string message)
    {
        if(!condition)throw new System.Exception("Oscilloscope dump check failed: "+message);
    }

    private static void EnsureProgram()
    {
        UdonSharpProgramAsset program=AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ControllerAsset);
        if(program==null)
        {
            MonoScript source=AssetDatabase.LoadAssetAtPath<MonoScript>(ControllerScript);
            Check(source!=null,"controller script imported");
            program=ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            program.sourceCsScript=source;
            AssetDatabase.CreateAsset(program,ControllerAsset);
        }
        AssetDatabase.SaveAssets();AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        UdonSharpProgramAsset.CompileAllCsPrograms(true);
    }

    private static Material EnsureMaterial(string path,string shaderName)
    {
        Material material=AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader=Shader.Find(shaderName);Check(shader!=null,"find "+shaderName);
        if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}
        else material.shader=shader;
        EditorUtility.SetDirty(material);return material;
    }

    private static Mesh EnsurePointMesh()
    {
        Mesh mesh=AssetDatabase.LoadAssetAtPath<Mesh>(PointMeshPath);
        if(mesh==null){mesh=new Mesh();mesh.name="OscilloscopePoint";AssetDatabase.CreateAsset(mesh,PointMeshPath);}
        mesh.Clear();
        mesh.vertices=new[]{Vector3.zero,Vector3.right};mesh.SetIndices(new[]{0,1},MeshTopology.Points,0);
        mesh.bounds=new Bounds(Vector3.zero,Vector3.one*2f);
        EditorUtility.SetDirty(mesh);return mesh;
    }

    private static DefaultControls.Resources Resources()
    {
        DefaultControls.Resources r=new DefaultControls.Resources();
        r.standard=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        r.background=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        r.knob=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");return r;
    }

    private static TMP_InputField InputNearest(Transform root,float y)
    {
        TMP_InputField[] fields=root.GetComponentsInChildren<TMP_InputField>(true);
        TMP_InputField best=null;float distance=float.MaxValue;
        foreach(TMP_InputField field in fields)
        {
            RectTransform rect=field.transform as RectTransform;if(rect==null)continue;
            float next=Mathf.Abs(rect.anchoredPosition.y-y);
            if(next<distance){distance=next;best=field;}
        }
        return distance<20f?best:null;
    }

    private static void CopyProxy(UdonSharpBehaviour behaviour)
    {
        UdonSharpEditorUtility.CopyProxyToUdon(behaviour,ProxySerializationPolicy.All);
        EditorUtility.SetDirty(behaviour);
        EditorUtility.SetDirty(UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour));
    }

    private static GameObject Waveform(Transform screen,string name,Mesh mesh,Material material)
    {
        Transform old=screen.Find(name);if(old!=null)Object.DestroyImmediate(old.gameObject);
        GameObject go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
        go.transform.SetParent(screen,false);go.transform.localPosition=new Vector3(0f,0f,-0.012f);
        go.GetComponent<MeshFilter>().sharedMesh=mesh;
        MeshRenderer renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;
        renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        return go;
    }

    [MenuItem("Tools/VRCSpice/Install oscilloscope output dump")]
    public static void BuildCurrentWorld()
    {
        EnsureProgram();
        var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
        GameObject oscillo=GameObject.Find("Oscillo");Check(oscillo!=null,"Oscillo root");
        Transform body=oscillo.transform.Find("Cube");Check(body!=null,"Oscillo/Cube");
        Transform solverTransform=body.Find("Solver");Transform ui=body.Find("OscUI");Transform screen=body.Find("Screen");
        Check(solverTransform!=null && ui!=null && screen!=null,"Solver, OscUI and Screen hierarchy");
        MNASolve solver=solverTransform.GetComponent<MNASolve>();Check(solver!=null,"MNASolve");

        Material extractor=EnsureMaterial(ExtractMaterialPath,"VRCSpice/Solver Output Dump");
        Material copy=EnsureMaterial(CopyMaterialPath,"VRCSpice/Oscilloscope Copy");
        Material ch1=EnsureMaterial("Assets/VRCSpice/Oscilloscope/CH1LineMaterial.mat","VRCSpice/Oscilloscope Waveform");
        Material ch2=EnsureMaterial("Assets/VRCSpice/Oscilloscope/CH2LineMaterial.mat","VRCSpice/Oscilloscope Waveform");
        ch1.SetInteger("_Channel",0);ch1.SetColor("_LineColor",new Color(0.02f,1f,0.02f,1f));
        ch2.SetInteger("_Channel",1);ch2.SetColor("_LineColor",new Color(1f,0.95f,0.02f,1f));

        SerializedObject solverObject=new SerializedObject(solver);
        solverObject.FindProperty("dumpExtractor").objectReferenceValue=extractor;
        SerializedProperty diagnostics=solverObject.FindProperty("outputMaterial");diagnostics.arraySize=2;
        diagnostics.GetArrayElementAtIndex(0).objectReferenceValue=AssetDatabase.LoadAssetAtPath<Material>("Assets/VRCSpice/FontAsset/font.mat");
        diagnostics.GetArrayElementAtIndex(1).objectReferenceValue=AssetDatabase.LoadAssetAtPath<Material>("Assets/VRCSpice/Debug.mat");
        solverObject.ApplyModifiedPropertiesWithoutUndo();CopyProxy(solver);

        MeshRenderer oldRenderer=solverTransform.GetComponent<MeshRenderer>();if(oldRenderer!=null)Object.DestroyImmediate(oldRenderer);
        MeshFilter oldFilter=solverTransform.GetComponent<MeshFilter>();if(oldFilter!=null)Object.DestroyImmediate(oldFilter);
        Transform oldGrid=solverTransform.Find("Grid");if(oldGrid!=null)Object.DestroyImmediate(oldGrid.gameObject);

        Transform previousScrollbar=ui.Find("scrollbar_divt");if(previousScrollbar!=null)Object.DestroyImmediate(previousScrollbar.gameObject);
        GameObject scrollbarObject=DefaultControls.CreateScrollbar(Resources());scrollbarObject.name="scrollbar_divt";
        scrollbarObject.transform.SetParent(ui,false);RectTransform scrollRect=scrollbarObject.GetComponent<RectTransform>();
        scrollRect.anchorMin=scrollRect.anchorMax=new Vector2(.5f,.5f);scrollRect.pivot=new Vector2(.5f,.5f);
        scrollRect.anchoredPosition=new Vector2(15f,-82f);scrollRect.sizeDelta=new Vector2(220f,22f);
        Scrollbar scrollbar=scrollbarObject.GetComponent<Scrollbar>();scrollbar.numberOfSteps=5;scrollbar.value=.5f;

        TMP_Text divText=null;Transform divTransform=ui.Find("divt_x10");
        if(divTransform==null)divTransform=ui.Find("divt_display");
        if(divTransform!=null)divText=divTransform.GetComponent<TMP_Text>();
        Check(divText!=null,"Time/div display text");divTransform.name="divt_display";
        divText.text="2 ms/div  x10";divText.fontSize=20;divText.alignment=TextAlignmentOptions.Center;divText.raycastTarget=false;
        RectTransform divRect=divTransform as RectTransform;divRect.sizeDelta=new Vector2(260f,36f);

        TMP_InputField probe1=InputNearest(ui,100f);TMP_InputField probe2=InputNearest(ui,50f);
        TMP_InputField yScale=InputNearest(ui,-118f);TMP_InputField oldX=InputNearest(ui,-149f);
        Check(probe1!=null && probe2!=null && yScale!=null,"legacy probe and Y scale fields");
        if(oldX!=null && oldX!=yScale)Object.DestroyImmediate(oldX.gameObject);

        OscilloscopeController controller=oscillo.GetComponent<OscilloscopeController>();
        if(controller==null)controller=oscillo.AddUdonSharpComponent<OscilloscopeController>();
        controller.solver=solver;controller.probeField1=probe1;controller.probeField2=probe2;controller.yScaleField=yScale;
        controller.timeDivScrollbar=scrollbar;controller.timeDivText=divText;
        controller.bufferTemplate=AssetDatabase.LoadAssetAtPath<RenderTexture>("Assets/VRCSpice/MNATools/Shader/SolverBufferTemplate.renderTexture");
        controller.copyMaterial=copy;controller.ch1Material=ch1;controller.ch2Material=ch2;
        CopyProxy(controller);
        var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(controller);
        UnityEventTools.AddStringPersistentListener(scrollbar.onValueChanged,backing.SendCustomEvent,nameof(OscilloscopeController.ChangeTimeDivision));

        Mesh point=EnsurePointMesh();Waveform(screen,"CH1Waveform",point,ch1);Waveform(screen,"CH2Waveform",point,ch2);
        EditorUtility.SetDirty(ch1);EditorUtility.SetDirty(ch2);EditorUtility.SetDirty(scrollbar);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        Debug.Log("Installed the 1000-sample Solver output dump and 200-point Oscillo display path.");
    }

    private static void VerifyShader(string path,params string[] properties)
    {
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
        Shader shader=AssetDatabase.LoadAssetAtPath<Shader>(path);Check(shader!=null,"load "+path);
        Check(!ShaderUtil.ShaderHasError(shader),"compile "+path);
        Material material=new Material(shader);foreach(string property in properties)Check(material.HasProperty(property),path+" "+property);
        Object.DestroyImmediate(material);
    }

    private static int Candidates(int start,int finish,int stride)
    {
        int first=start%stride==0?start:start+stride-start%stride;
        int last=finish-finish%stride;return last<first?0:(last-first)/stride+1;
    }

    [MenuItem("Tools/VRCSpice/Build and verify oscilloscope output dump")]
    public static void BuildAndVerify()
    {
        BuildCurrentWorld();
        Check(Candidates(1,1000,1)==1000,"x1 candidates");Check(Candidates(1,1000,5)==200,"x5 candidates");
        Check(Candidates(1,1000,10)==100,"x10 candidates");Check(Candidates(1,1000,50)==20,"x50 candidates");
        Check(Candidates(1,1000,100)==10,"x100 candidates");
        string variables=File.ReadAllText("Assets/VRCSpice/MNATools/Shader/Solver_variables.hlsl");
        Check(variables.Contains("OUTPUT_BUFFER_LENGTH         1000u"),"1000 point solver history");
        VerifyShader("Assets/VRCSpice/MNATools/Shader/SolverOutputDump.shader","_MainTex","_DATA_N","_DumpGeneration");
        string dumpShader=File.ReadAllText("Assets/VRCSpice/MNATools/Shader/SolverOutputDump.shader");
        Check(dumpShader.Contains("input.vertex.xy"),"solver dump uses destination pixel coordinates");
        string solverSource=File.ReadAllText("Assets/VRCSpice/MNATools/MNASolve.cs");
        Check(solverSource.Contains("SetInteger(\"_SrcMatSize\", matrixsize)"),"circuit changes preserve completed solver state");
        string controllerSource=File.ReadAllText(ControllerScript);
        Check(controllerSource.Contains("bool rowChanged") && controllerSource.Contains("AdvanceConfiguration();"),
            "probe row resolution rebuilds the display");
        Check(controllerSource.Contains("VerticalDivisionCount = 8f") &&
            controllerSource.Contains("1f / (VerticalDivisionCount"),
            "voltage scale uses the eight-division screen height");
        string copyShader=File.ReadAllText("Assets/VRCSpice/Oscilloscope/OscilloscopeCopy.shader");
        Check(copyShader.Contains("oldConfig || generationChanged"),"dump generation rebuilds without duplicate samples");
        VerifyShader("Assets/VRCSpice/Oscilloscope/OscilloscopeCopy.shader","_SolverDump","_Stride","_ConfigRevision");
        VerifyShader("Assets/VRCSpice/Oscilloscope/OscilloscopeWaveform.shader","_DisplayDump","_Channel","_YScale");
        Mesh point=AssetDatabase.LoadAssetAtPath<Mesh>(PointMeshPath);
        Check(point!=null && point.vertexCount==2 && point.GetTopology(0)==MeshTopology.Points,
            "two-segment waveform point mesh");
        VerifyShader("Assets/VRCSpice/BreadBoard/Shaders/LedEmission.shader","_MainTex","_DATA_N","_OutputDeltaTime");
        PredictorlessSolverVerification.Run();BreadboardVerification.Run();
        Debug.Log("Oscilloscope output dump verification PASSED.");
    }
}
