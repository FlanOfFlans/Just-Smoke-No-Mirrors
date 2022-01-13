using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class FullyParallelFluid : MonoBehaviour
{
    public ComputeShader s;
    public GameObject renderTarget;

	[SerializeField]
	int resolution;

    int addID;
    int diffuseID;
    int divergenceID;
    int poissonID;
    int applyPoissonID;
    int advectVID;
    int advectDID;
    int visualizeGrayID;
    int visualizeRgbID;
    int boundsContinuityID;
    int boundsReflectionHID;
    int boundsReflectionVID;

    float[] working;
    public float   viscosity;
    public float   diffusability;
    public float   visualizationMax;
    public float   colorValue;
    public float[] xForcesArr;
    public float[] yForcesArr;
    public float[] sourcesArr;
    ComputeBuffer xForces;
    ComputeBuffer yForces;
    ComputeBuffer sources;
    ComputeBuffer xVelocity;
    ComputeBuffer xVelocityAux;
    ComputeBuffer yVelocity;
    ComputeBuffer yVelocityAux;
    ComputeBuffer density;
    ComputeBuffer densityAux;
    ComputeBuffer divergence;
    ComputeBuffer poisson;

    RenderTexture tex; // Corresponds to tex, used in visualization

    int groupCount;
    int frame = 0;

    enum BoundMode { Continuity, ReflectH, ReflectV };

    // Start is called before the first frame update
    void Start()
    {
        if(resolution % 8 != 0) 
        { throw new System.ArgumentException("Resolution must be multiple of 8"); }

        Dictionary<string, int> kernels;

        InitializeArrays();
        kernels = InitializeKernels();
        InitializeBuffers(kernels);
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        frame += 1;

        sourcesArr[26] = 10f;
        xForcesArr[26] = 2000f;
        yForcesArr[26] = 2000f;

        sourcesArr[297] = 10f;
        xForcesArr[297] = -2000f;
        yForcesArr[297] = -2000f;

        s.SetFloat("dt", Time.deltaTime);
        s.SetFloat("visualizationMax", visualizationMax);
        s.SetFloat("viscosity", viscosity);
        s.SetFloat("diffusability", diffusability); 
        sources.SetData(sourcesArr);
        xForces.SetData(xForcesArr);
        yForces.SetData(yForcesArr);
        

        // Advance independent parts of velocity and density
        s.Dispatch(addID, groupCount, groupCount, 1);
        s.Dispatch(diffuseID, groupCount, groupCount, 1);

        // Project velocity with Poisson matrix
        s.Dispatch(divergenceID, groupCount, groupCount, 1);
        s.Dispatch(poissonID, groupCount, groupCount, 1);
        s.Dispatch(applyPoissonID, groupCount, groupCount, 1);

        // Advect velocity--Density's advection is dependent on this.
        s.Dispatch(advectVID, groupCount, groupCount, 1);

        // Project again for accuracy.
        s.Dispatch(divergenceID, groupCount, groupCount, 1);
        s.Dispatch(poissonID, groupCount, groupCount, 1);
        s.Dispatch(applyPoissonID, groupCount, groupCount, 1);

        // Advect density--dependent on velocity's advection.
        s.Dispatch(advectDID, groupCount, groupCount, 1);

        // Write to texture.
        s.Dispatch(visualizeGrayID, groupCount, groupCount, 1);
    }

    void InitializeArrays()
    {
        // Create all the necessary arrays
    	xForcesArr =    new float[(resolution+2) * (resolution+2)];
        yForcesArr =    new float[(resolution+2) * (resolution+2)];
    	sourcesArr =    new float[(resolution+2) * (resolution+2)];
    }

    Dictionary<string, int> InitializeKernels()
    {
    	Dictionary<string, int> kernels = new Dictionary<string, int>();

    	// Get kernel IDs
        addID               = s.FindKernel("addSources");
        diffuseID           = s.FindKernel("diffuse");
        divergenceID        = s.FindKernel("calculateDivergence");
        poissonID           = s.FindKernel("poisson");
        applyPoissonID      = s.FindKernel("applyPoisson");
        advectVID           = s.FindKernel("advectV");
        advectDID           = s.FindKernel("advectD");
        visualizeGrayID     = s.FindKernel("visualizeGrayscale");
        visualizeRgbID      = s.FindKernel("visualizeRGB");
        boundsContinuityID  = s.FindKernel("boundsContinuity");
        boundsReflectionHID = s.FindKernel("boundsReflectionH");
        boundsReflectionVID = s.FindKernel("boundsReflectionV");
       
        // Print ID names
        kernels = new Dictionary<string, int>()
        {
            { "add",             addID },
            { "diffuse",         diffuseID },
            { "divergence",      divergenceID },
            { "poisson",         poissonID },
            { "applyPoisson",    applyPoissonID },
            { "advectVelocity",  advectVID },
            { "advectDensity",   advectDID },
            { "visualizeGrayID", visualizeGrayID },
            { "visualizeRgbID",  visualizeRgbID },
            { "boundsContinuityID", boundsContinuityID },
            { "boundsReflectionHID", boundsReflectionHID },
            { "boundsReflectionVID", boundsReflectionVID },
        };

        if(Debug.isDebugBuild)
        {
            foreach (KeyValuePair<string, int> kernel in kernels)
            {
                print(kernel.Key + " : " + kernel.Value.ToString());
            } 
        }

        return kernels;
    }

    void InitializeBuffers(Dictionary<string, int> kernels)
    {
        // Set up buffers
        xForces      = new ComputeBuffer((resolution+2) * (resolution+2), 4); // float bytes
        yForces      = new ComputeBuffer((resolution+2) * (resolution+2), 4); // float bytes
        sources      = new ComputeBuffer((resolution+2) * (resolution+2), 4); // float bytes
        xVelocity    = new ComputeBuffer((resolution+2) * (resolution+2), 4); // float bytes
        xVelocityAux = new ComputeBuffer((resolution+2) * (resolution+2), 4); // float bytes
        yVelocity    = new ComputeBuffer((resolution+2) * (resolution+2), 4); // float bytes
        yVelocityAux = new ComputeBuffer((resolution+2) * (resolution+2), 4); // float bytes
        density      = new ComputeBuffer((resolution+2) * (resolution+2), 4); // float bytes
        densityAux   = new ComputeBuffer((resolution+2) * (resolution+2), 4); // float bytes
        poisson      = new ComputeBuffer((resolution+2) * (resolution+2), 4); // float bytes

        xForces.name = "xForces";
        yForces.name = "yForces";
        sources.name = "sources";
        xVelocity.name = "xVelocity";
        xVelocityAux.name = "xVelocityAux";
        yVelocity.name = "yVelocity";
        yVelocityAux.name = "yVelocityAux";
        density.name = "density";
        densityAux.name = "densityAux";
        poisson.name = "poisson";

        // Assign buffers
        foreach(KeyValuePair<string, int> kernel in kernels)
        {
            s.SetBuffer(kernel.Value, "xForces", xForces);
            s.SetBuffer(kernel.Value, "yForces", yForces);
            s.SetBuffer(kernel.Value, "sources", sources);
            s.SetBuffer(kernel.Value, "xVelocity", xVelocity);
            s.SetBuffer(kernel.Value, "xVelocityAux", xVelocityAux);
            s.SetBuffer(kernel.Value, "yVelocity", yVelocity);
            s.SetBuffer(kernel.Value, "yVelocityAux", yVelocityAux);
            s.SetBuffer(kernel.Value, "density", density);
            s.SetBuffer(kernel.Value, "densityAux", densityAux);    
            s.SetBuffer(kernel.Value, "poisson", poisson);
        }
        
        // Set up texture
        tex = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        tex.enableRandomWrite = true;        
        tex.filterMode = FilterMode.Point;
        tex.Create();
        tex.name = "Output texture";
        s.SetTexture(visualizeGrayID, "tex", tex);
        s.SetTexture(visualizeRgbID, "tex", tex);

        groupCount = resolution / 8;

        renderTarget.GetComponent<MeshRenderer>().material.SetTexture("_MainTex", tex);

        s.SetInt("resolution", resolution);
        s.SetInt("groupCount", groupCount);
    }

    void OnDestroy()
    {
        xForces.Release();
        yForces.Release();
        sources.Release();
        xVelocity.Release();
        xVelocityAux.Release();
        yVelocity.Release();
        yVelocityAux.Release();
        density.Release();
        densityAux.Release();
        divergence.Release();
        poisson.Release();
    }
}
