using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VectorField
{
	public Queue<Change> changes;

	readonly int minX;
	readonly int maxX;
	readonly int minY;
	readonly int maxY;

	Vector2[] vectors;

	public delegate Vector2 VectorFieldFunc(int x, int y);



	public VectorField(int minX, int maxX, int minY, int maxY)
	{
		int arraySize;

		if(minX > maxX || minY > maxY)
		{
			throw new System.ArgumentException("Min bounds cannot be greater than max bounds");
		}

		changes = new Queue<Change>();

		this.minX = minX;
		this.maxX = maxX;
		this.minY = minY;
		this.maxY = maxY;

		arraySize = (maxX - minX + 1) * (maxY - minY + 1);
		vectors = new Vector2[arraySize];

		for(int i = 0; i < arraySize; i++)
		{
			vectors[i] = new Vector2(0,0);
		}
	}


	public Vector2 getVec(int x, int y)
	{
		UnityEngine.Debug.Log(x);
		UnityEngine.Debug.Log(y);
		return vectors[coordToIndex(x, y)];
	}

	public void setVec(int x, int y, Vector2 newVec)
	{
		vectors[coordToIndex(x, y)] = newVec;
		changes.Enqueue(new Change(x, y, newVec));
	}

	public void applyFunction(VectorFieldFunc f)
	{
		for(int j = minY; j <= maxY; j++)
		{
			for(int i = minX; i <= maxX; i++)
			{
				setVec(i, j, f(i, j));
			}
		}
	}


	private int coordToIndex(int x, int y)
	{
        return (y-minY)*(maxY-minY+1) + (x-minX);
	}
}

public struct Change
{
	public readonly int x;
	public readonly int y;
	public readonly Vector2 changedTo;

	public Change(int x, int y, Vector2 changedTo)
	{
		this.x = x;
		this.y = y;
		this.changedTo = changedTo;
	}
}