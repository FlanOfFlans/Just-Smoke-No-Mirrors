using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Arrow : MonoBehaviour
{
	public Mesh arrow;
	public Mesh ball;
	public bool debug = false;

    [SerializeField]
    Vector2 vec;
	MeshFilter m;
	Vector2 prevVec = Vector2.negativeInfinity;
	VectorFieldObject field;
	Material mat;
	Color minMagColor;
	Color maxMagColor;
	float minMag;
	float maxMag;



    public Vector2 getVec()
    {
        return vec;
    }

    public void setVec(Vector2 newVec)
    {
        vec = newVec;

        updateAngle();
    }

    public void Initialize()
    {
        m = GetComponent<MeshFilter>();
        m.mesh = arrow;
        field = transform.parent.GetComponent<VectorFieldObject>();
        mat = GetComponent<Renderer>().material;
        minMagColor = field.minMagnitudeColor;
        maxMagColor = field.maxMagnitudeColor;
    }

    // Start is called before the first frame update
    void Start()
    {
        Initialize();
    }

    // Update is called once per frame
    void Update()
    {
        if(debug)
        {
            updateAngle();
        }

		minMag = field.magColorLowerBound;
        maxMag = field.magColorUpperBound;

    	float colorMag = vec.magnitude;
    	colorMag = Mathf.Clamp(colorMag, field.magColorLowerBound, field.magColorUpperBound);

    	float t = (colorMag - minMag) / (maxMag - minMag);
    	Color c = lerpRGB(minMagColor, maxMagColor, t);

    	mat.SetColor("_Color", c);
    }

    void setAngle(float angle)
    {
    	Vector3 rotation = transform.eulerAngles;
    	rotation.y = angle;
    	transform.eulerAngles = rotation;
    }

    void updateAngle()
    {
        if(vec == prevVec)
        {
            return;
        }

        prevVec = vec;
    	
        if(vec.x == 0 && vec.y == 0)
    	{
    		m.mesh = ball;
    	}

    	else if(vec.x == 0 && vec.y > 0)
    	{
    		m.mesh = arrow;
    		setAngle(0);
    	}

    	else if(vec.x == 0 && vec.y < 0)
    	{
    		m.mesh = arrow;
    		setAngle(180);
    	}

    	else if(vec.y == 0 && vec.x > 0)
    	{
    		m.mesh = arrow;
    		setAngle(90);
    	}

    	else if(vec.y == 0 && vec.x < 0)
    	{
    		m.mesh = arrow;
    		setAngle(-90);
    	}

    	else
    	{
    		float offset;

    		if(vec.x > 0)
    		{
    			offset = 90;
    		}

    		else
    		{
    			offset = -90;
    		}
 
    		m.mesh = arrow;
    		float angle = -Mathf.Rad2Deg * Mathf.Atan(vec.y / vec.x);
    		setAngle(angle + offset);
    	}
    }

    Color lerpRGB(Color startColor, Color endColor, float t)
    {
    	Color newColor = new Color(0f,0f,0f,1f);

    	newColor.r = Mathf.Lerp(startColor.r, endColor.r, t);
    	newColor.g = Mathf.Lerp(startColor.g, endColor.g, t);
    	newColor.b = Mathf.Lerp(startColor.b, endColor.b, t);

    	return newColor;
    }
}
