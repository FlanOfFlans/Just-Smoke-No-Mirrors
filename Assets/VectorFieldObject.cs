using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VectorFieldObject : MonoBehaviour
{
	public float magColorLowerBound = 0;
	public float magColorUpperBound = 15;
	public Color minMagnitudeColor;
	public Color maxMagnitudeColor;
    public GameObject arrowPrefab;
    public bool debug;

    [SerializeField]
    int minX, maxX, minY, maxY;
    VectorField field;
    Arrow[] arrows;



    // Start is called before the first frame update
    void Start()
    {
        field = new VectorField(minX, maxX, minY, maxY);
        arrows = new Arrow[(maxX - minX + 1) * (maxY - minY + 1)];

        for(int j = minY; j <= maxY; j++)
        {
            for(int i = minX; i <= maxX; i++)
            {
                GameObject newArrow;
                Transform t;
                Arrow a;

                newArrow = Instantiate(arrowPrefab);
                t = newArrow.transform;
                t.localPosition = new Vector3(i, 0, j);
                t.SetParent(transform);
                newArrow.name = System.String.Format("({0},{1})", i, j);
                
                a = newArrow.GetComponent<Arrow>();
                a.Initialize();
                a.setVec(new Vector2(0,0));
                a.debug = debug;

                arrows[coordToIndex(i,j)] = a;
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        while(field.changes.Count != 0)
        {
            Arrow a;
            Change c;

            c = field.changes.Dequeue();
            a = arrows[coordToIndex(c.x, c.y)];
            a.setVec(c.changedTo);
        }
    }

    public void ApplyFunction(VectorField.VectorFieldFunc f)
    {
        field.applyFunction(f);
    }

    public void ApplyCurlTest()
    {
        Vector2 Curl(int x, int y)
        {
            return new Vector2(-y, x);
        }

        VectorField.VectorFieldFunc curl = new VectorField.VectorFieldFunc(Curl);
        ApplyFunction(curl);
    }

    private int coordToIndex(int x, int y)
    {
        return (y-minY)*(maxY-minY+1) + (x-minX);
    }
}
