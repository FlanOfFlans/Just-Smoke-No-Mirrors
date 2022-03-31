using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Fluid : MonoBehaviour
{
    public ComputeShader s;
    public GameObject renderTarget;

    public bool parallelAdd;
    public bool parallelDiffuse;
    public bool parallelDivergence;
    public bool parallelPoisson;
    public bool parallelApplyPoisson;

	[SerializeField]
	int resolution;

    int addID;
    int diffuseID;
    int divergenceID;
    int poissonID;
    int applyPoissonHID;
    int applyPoissonVID;
    int advectID;
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
    public float[] xForces;
    public float[] yForces;
    public float[] sources;
    public float[] xVelocity;
    public float[] yVelocity;
    public float[] density;
    public float[] divergence;
    public float[] poisson;

    ComputeBuffer output; // Corresponds to workingArr
    ComputeBuffer input1; // Corresponds to readArr1
    ComputeBuffer input2; // Corresponds to readArr2 
    ComputeBuffer input3; // Corresponds to readArr3, used in advection
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

        // Set a few other values that don't fit into the other steps
        s.SetInt("resolution", resolution);
        s.SetInt("groupCount", groupCount);
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        frame += 1;

        sources[26] = 10f;
        xForces[26] = 2000f;
        yForces[26] = 2000f;

        sources[297] = 10f;
        xForces[297] = -2000f;
        yForces[297] = -2000f;

        s.SetFloat("dt", Time.deltaTime);
        s.SetFloat("visualizationMax", visualizationMax);
        
        VelocityStep();
        DensityStep();

        Visualize(density);
    }

    void VelocityStep()
    {
        s.SetFloat("diffusionRate", viscosity);

        Add(xVelocity, xForces);
        Add(yVelocity, yForces);

        Diffuse(xVelocity, BoundMode.ReflectH, viscosity, true);
        Diffuse(yVelocity, BoundMode.ReflectV, viscosity, true);

        ProjectVelocity();

        Advect(xVelocity, BoundMode.ReflectH);
        Advect(yVelocity, BoundMode.ReflectV);

        ProjectVelocity();
    }

    void DensityStep()
    {
        s.SetFloat("diffusionRate", diffusability);
        
        Add(density, sources);
        Diffuse(density, BoundMode.Continuity, diffusability, true);
        Advect(density, BoundMode.Continuity);
    }

    // Adds the field term2 to the field term1, scaling term2 by dt.
    void Add(float[] term1, float[] term2)
    {
    	if(parallelAdd)
    	{
	        output.SetData(term1);
	        input1.SetData(term2);
	        s.Dispatch(addID, groupCount, groupCount, 1);
	        output.GetData(term1);    		
    	}

    	else
    	{

    		float[] result = new float[(resolution+2) * (resolution+2)];
    		for(int j = 1; j <= resolution; j++)
    		{
    			for(int i = 1; i <= resolution; i++)
    			{
    				int index = j*(resolution+2) + i;
    				float value1 = term1[index];
    				float value2 = term2[index];
    				term1[index] = value1 + value2 * Time.deltaTime;
    			}
    		}
    	}
    }

    void Diffuse(float[] diffuseArray, BoundMode boundaryMode, float resistance, bool useParallel)
    { 
        float[] prevField = new float[(resolution+2) * (resolution+2)];
        System.Array.Copy(diffuseArray, prevField, (resolution+2) * (resolution+2));

		if(parallelDiffuse && useParallel)
		{
			switch(boundaryMode)
            {
                case BoundMode.ReflectH:
                    s.SetInt("linsolveBoundsMode", 0);
                    break;

                case BoundMode.ReflectV:
                    s.SetInt("linsolveBoundsMode", 1);
                    break;

                case BoundMode.Continuity:
                    s.SetInt("linsolveBoundsMode", 2);
                    break;

                default:
                    throw new System.ArgumentException("Invalid bounds mode");

            }

			s.SetFloat("diffusionRate", resistance);
	        output.SetData(diffuseArray);
	        input1.SetData(prevField);
            s.Dispatch(diffuseID, groupCount, groupCount, 1);
	        output.GetData(diffuseArray);

		} 

		else
		{
            // Gauss-Seidel solver for diffusion.
            // Increase k's max value to increase approximation accuracy.
	        for(int k = 0; k < 20; k++)
	        {
	            for(int i = 1; i <= resolution; i++)
	            {
	                for(int j = 1; j <= resolution; j++)
	                {
	                    float diff = Time.deltaTime*resistance*resolution*resolution;
	                    float center = prevField[get2DTo1D(i, j)];
	                    float left = diffuseArray[get2DTo1D(i - 1, j)];
	                    float right = diffuseArray[get2DTo1D(i + 1, j)];
	                    float up = diffuseArray[get2DTo1D(i, j - 1)];
	                    float down = diffuseArray[get2DTo1D(i, j + 1)];

	                    float v = (center + diff*(left + right + up + down)) / (1 + 4 * diff);

	                    diffuseArray[get2DTo1D(i, j)] = v;
	                }
	            }
	            Bounds(diffuseArray, boundaryMode);
	        }
		}
    }

    // Enforces conservation of mass, and thereby, pressure.
    void ProjectVelocity()
    {
        CalculateDivergence();
        CalculatePoisson();
        ApplyPoisson();
    }

    void CalculateDivergence()
    {
    	if(parallelDivergence)
    	{
	        input1.SetData(xVelocity);
	        input2.SetData(yVelocity);
	        s.Dispatch(divergenceID, groupCount, groupCount, 1);
	        output.GetData(divergence);   		
    	}

    	else
    	{
			float h = 1.0f / resolution;
	        for(int i = 1; i <= resolution; i++)
	        {
	            for(int j = 1; j <= resolution; j++)
	            {
	                float left = xVelocity[get2DTo1D(i - 1, j)];
	                float right = xVelocity[get2DTo1D(i + 1, j)];
	                float up = yVelocity[get2DTo1D(i, j - 1)];
	                float down = yVelocity[get2DTo1D(i, j + 1)];

	                divergence[get2DTo1D(i, j)] = -0.5f * h * (right - left + down - up);
	            }
	        }
    	}
        
        Bounds(divergence, BoundMode.Continuity);
    }

    void CalculatePoisson()
    {
        System.Array.Clear(poisson, 0, poisson.Length);

        // Calculate Poisson matrix via Gauss-Seidel
        
        if(parallelPoisson)
        {
            output.SetData(poisson);
            input1.SetData(divergence);
            s.Dispatch(poissonID, groupCount, groupCount, 1);
            output.GetData(poisson);
        }

        else
        {
        // Iterative version
            for(int k = 0; k < 20; k++)
            {
                for(int i = 1; i <= resolution; i++)
                {
                    for(int j = 1; j <= resolution; j++)
                    {
                        float div = divergence[get2DTo1D(i, j)];
                        float left = poisson[get2DTo1D(i - 1, j)];
                        float right = poisson[get2DTo1D(i + 1, j)];
                        float up = poisson[get2DTo1D(i, j - 1)];
                        float down = poisson[get2DTo1D(i, j + 1)];

                        poisson[get2DTo1D(i, j)] = (div + left + right + up + down) / 4.0f;
                    }
                }
                Bounds(poisson, BoundMode.Continuity);
            }
        }
    }

    void ApplyPoisson()
    {
        // Iterative

        if(parallelApplyPoisson)
        {
	        output.SetData(xVelocity);
	        input1.SetData(poisson);
	        s.Dispatch(applyPoissonHID, groupCount, groupCount, 1);
	        output.GetData(xVelocity);

	        output.SetData(yVelocity);
	        s.Dispatch(applyPoissonVID, groupCount, groupCount, 1);
	        output.GetData(yVelocity); 
        }

        else
        {
	        float h = 1.0f / resolution;
	        for(int i = 1; i <= resolution; i++)
	        {
	            for(int j = 1; j <= resolution; j++)
	            {
	                float left = poisson[get2DTo1D(i - 1, j)];
	                float right = poisson[get2DTo1D(i + 1, j)];
	                float up = poisson[get2DTo1D(i, j - 1)];
	                float down = poisson[get2DTo1D(i, j + 1)];
	                xVelocity[get2DTo1D(i, j)] -= 0.5f * (right - left) / h;
	                yVelocity[get2DTo1D(i, j)] -= 0.5f * (down - up) / h;
	            }
	        }
	    }
        
        Bounds(xVelocity, BoundMode.ReflectH);
        Bounds(yVelocity, BoundMode.ReflectV);
    }

    void Advect(float[] advectField, BoundMode boundaryMode)
    {
        input1.SetData(xVelocity);
        input2.SetData(yVelocity);
        input3.SetData(advectField);
        s.Dispatch(advectID, groupCount, groupCount, 1);
        output.GetData(advectField);

        Bounds(advectField, boundaryMode);
    }

    void Visualize(float[] field)
    {
        input1.SetData(field);
        s.Dispatch(visualizeGrayID, groupCount, groupCount, 1);
    }

    void Bounds(float[] field, BoundMode mode)
    {
        if(mode == BoundMode.ReflectH)
        {
            for(int i = 1; i <= resolution; i++)
            {
                field[get2DTo1D(0, i)] = -field[get2DTo1D(1, i)];
                field[get2DTo1D(resolution+1, i)] = -field[get2DTo1D(resolution, i)];
                field[get2DTo1D(i, 0)] = field[get2DTo1D(i, 1)];
                field[get2DTo1D(i, resolution+1)] = field[get2DTo1D(i, resolution)];
            }

           	int xStep = 1;
	        int yStep = resolution + 2;
	        int ULCorner = get2DTo1D(0, 0);
	        int URCorner = get2DTo1D(resolution+1, 0);
	        int DLCorner = get2DTo1D(0, resolution+1);
	        int DRCorner = get2DTo1D(resolution+1, resolution+1);

	        field[ULCorner] = 0.5f * field[ULCorner + xStep] + field[ULCorner + yStep];
	        field[URCorner] = 0.5f * field[URCorner - xStep] + field[URCorner + yStep];
	        field[DLCorner] = 0.5f * field[DLCorner + xStep] + field[DLCorner - yStep];
	        field[DRCorner] = 0.5f * field[DRCorner - xStep] + field[DRCorner - yStep];
        }

        if(mode == BoundMode.ReflectV)
        {
            for(int i = 1; i <= resolution; i++)
            {
                field[get2DTo1D(0, i)] = field[get2DTo1D(1, i)];
                field[get2DTo1D(resolution+1, i)] = field[get2DTo1D(resolution, i)];
                field[get2DTo1D(i, 0)] = -field[get2DTo1D(i, 1)];
                field[get2DTo1D(i, resolution+1)] = -field[get2DTo1D(i, resolution)];
            }

           	int xStep = 1;
	        int yStep = resolution + 2;
	        int ULCorner = get2DTo1D(0, 0);
	        int URCorner = get2DTo1D(resolution+1, 0);
	        int DLCorner = get2DTo1D(0, resolution+1);
	        int DRCorner = get2DTo1D(resolution+1, resolution+1);

	        field[ULCorner] = 0.5f * field[ULCorner + xStep] + field[ULCorner + yStep];
	        field[URCorner] = 0.5f * field[URCorner - xStep] + field[URCorner + yStep];
	        field[DLCorner] = 0.5f * field[DLCorner + xStep] + field[DLCorner - yStep];
	        field[DRCorner] = 0.5f * field[DRCorner - xStep] + field[DRCorner - yStep];
        }

        if(mode == BoundMode.Continuity)
        {
            for(int i = 0; i <= resolution; i++)
            {
                field[get2DTo1D(0, i)] = field[get2DTo1D(1, i)];
                field[get2DTo1D(resolution+1, i)] = field[get2DTo1D(resolution, i)];
                field[get2DTo1D(i, 0)] = field[get2DTo1D(i, 1)];
                field[get2DTo1D(i, resolution+1)] = field[get2DTo1D(i, resolution)];
            }

	        int xStep = 1;
	        int yStep = resolution + 2;
	        int ULCorner = get2DTo1D(0, 0);
	        int URCorner = get2DTo1D(resolution+1, 0);
	        int DLCorner = get2DTo1D(0, resolution+1);
	        int DRCorner = get2DTo1D(resolution+1, resolution+1);

            field[ULCorner] = 0.5f * field[ULCorner + xStep] + field[ULCorner + yStep];
	        field[URCorner] = 0.5f * field[URCorner - xStep] + field[URCorner + yStep];
	        field[DLCorner] = 0.5f * field[DLCorner + xStep] + field[DLCorner - yStep];
	        field[DRCorner] = 0.5f * field[DRCorner - xStep] + field[DRCorner - yStep];
        }
    }

    int get2DTo1D(int x, int y)
    {
        return (resolution + 2) * y + x;
    }

    void InitializeArrays()
    {
        // Create all the necessary arrays
    	xForces =    new float[(resolution+2) * (resolution+2)];
        yForces =    new float[(resolution+2) * (resolution+2)];
    	sources =    new float[(resolution+2) * (resolution+2)];
    	working =    new float[(resolution+2) * (resolution+2)];
       	xVelocity =  new float[(resolution+2) * (resolution+2)];
        yVelocity =  new float[(resolution+2) * (resolution+2)];
        density =    new float[(resolution+2) * (resolution+2)];
        divergence = new float[(resolution+2) * (resolution+2)];
        poisson =    new float[(resolution+2) * (resolution+2)];
    }

    Dictionary<string, int> InitializeKernels()
    {
    	Dictionary<string, int> kernels = new Dictionary<string, int>();

    	// Get kernel IDs
        addID               = s.FindKernel("addSources");
        diffuseID           = s.FindKernel("diffuseStep");
        divergenceID        = s.FindKernel("calculateDivergence");
        poissonID           = s.FindKernel("poissonStep");
        applyPoissonHID     = s.FindKernel("applyPoissonMatrixHorizontal");
        applyPoissonVID     = s.FindKernel("applyPoissonMatrixVertical");
        advectID            = s.FindKernel("advect");
        visualizeGrayID     = s.FindKernel("visualizeGrayscale");
        visualizeRgbID      = s.FindKernel("visualizeRGB");
        boundsContinuityID  = s.FindKernel("boundsContinuity");
        boundsReflectionHID = s.FindKernel("boundsReflectionH");
        boundsReflectionVID = s.FindKernel("boundsReflectionV");
       
        kernels = new Dictionary<string, int>()
        {
            { "add",             addID },
            { "diffuse",         diffuseID },
            { "divergence",      divergenceID },
            { "poisson",         poissonID },
            { "applyPoissonH",   applyPoissonHID },
            { "applyPoissonV",   applyPoissonVID },
            { "advect",          advectID },
            { "visualizeGrayID", visualizeGrayID },
            { "visualizeRgbID", visualizeRgbID },
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
        output = new ComputeBuffer((resolution+2) * (resolution+2), 4); // float bytes
        input1 = new ComputeBuffer((resolution+2) * (resolution+2), 4); // float bytes
        input2 = new ComputeBuffer((resolution+2) * (resolution+2), 4); // float bytes
        input3 = new ComputeBuffer((resolution+2) * (resolution+2), 4); // float bytes
        output.name = "Working Array";
        input1.name = "Input 1";
        input2.name = "Input 2";
        input3.name = "Input 3";

        // Assign buffers
        s.SetBuffer(advectID, "readArr3", input3);
        foreach(KeyValuePair<string, int> kernel in kernels)
        {
            s.SetBuffer(kernel.Value, "workingArr", output);
            s.SetBuffer(kernel.Value, "readArr1", input1);
            s.SetBuffer(kernel.Value, "readArr2", input2);            
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

        // Ensure our display object uses the texture
        renderTarget.GetComponent<MeshRenderer>().material.SetTexture("_MainTex", tex);
    }

    void OnDestroy()
    {
        output.Release();
        input1.Release();
        input2.Release();
        input3.Release();
    }
}
