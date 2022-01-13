using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public abstract class FieldFunction : ScriptableObject
{
	public abstract Vector2 f(float x, float y);
}