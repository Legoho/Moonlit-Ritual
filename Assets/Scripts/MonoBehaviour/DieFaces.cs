using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reads the invisible "Face_1" ... "Face_8" marker objects exported with the D8 models
/// and answers "which number is facing up?" and "how do I rotate the die so number X faces up?".
/// Added automatically by DiceRollVisualizer; you don't need to put it on anything yourself.
/// </summary>
public class DieFaces : MonoBehaviour
{
    // Face normals and values in this transform's local space.
    public Vector3[] localNormals;
    public int[] faceValues;
    public Vector3 localCenter;

    public bool Initialize()
    {
        var rend = GetComponentInChildren<Renderer>();
        Vector3 worldCenter = rend != null ? rend.bounds.center : transform.position;
        localCenter = transform.InverseTransformPoint(worldCenter);

        var normals = new List<Vector3>();
        var values = new List<int>();
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("Face_")) continue;
            if (!int.TryParse(t.name.Substring(5), out int value)) continue;
            Vector3 dir = transform.InverseTransformPoint(t.position) - localCenter;
            if (dir.sqrMagnitude < 1e-12f) continue;
            normals.Add(dir.normalized);
            values.Add(value);
        }

        localNormals = normals.ToArray();
        faceValues = values.ToArray();

        if (localNormals.Length < 8)
        {
            Debug.LogError($"[DieFaces] '{name}' has {localNormals.Length} Face_N markers (expected 8). " +
                           "Use the D8_*.fbx files exported with face markers.", this);
            return false;
        }
        return true;
    }

    /// <summary>The value whose face points most toward world up. alignment = 1 means perfectly flat.</summary>
    public int TopFace(out float alignment)
    {
        int best = -1;
        alignment = -2f;
        for (int i = 0; i < localNormals.Length; i++)
        {
            float d = transform.TransformDirection(localNormals[i]).y;
            if (d > alignment) { alignment = d; best = i; }
        }
        return best >= 0 ? faceValues[best] : 0;
    }

    public Vector3 NormalFor(int value)
    {
        for (int i = 0; i < faceValues.Length; i++)
            if (faceValues[i] == value) return localNormals[i];
        return Vector3.zero;
    }

    /// <summary>
    /// Finds a rotation that is a true symmetry of the die (so it still sits flat in exactly the same pose)
    /// and moves face `wanted` to where face `current` is.
    /// </summary>
    public bool TryGetSwapRotation(int wanted, int current, out Quaternion rotation)
    {
        rotation = Quaternion.identity;
        if (wanted == current) return true;

        Vector3 from = NormalFor(wanted);
        Vector3 to = NormalFor(current);
        if (from == Vector3.zero || to == Vector3.zero) return false;

        // A second reference face that is not parallel to `from`.
        Vector3 refFace = Vector3.zero;
        foreach (Vector3 n in localNormals)
        {
            float d = Vector3.Dot(n, from);
            if (d > -0.9f && d < 0.9f) { refFace = n; break; }
        }
        if (refFace == Vector3.zero) return false;

        float refAngle = Vector3.Dot(from, refFace);
        Quaternion fromBasis = Quaternion.LookRotation(from, refFace);

        foreach (Vector3 candidate in localNormals)
        {
            if (Mathf.Abs(Vector3.Dot(to, candidate) - refAngle) > 0.02f) continue;

            Quaternion q = Quaternion.LookRotation(to, candidate) * Quaternion.Inverse(fromBasis);
            if (MapsFacesOntoFaces(q))
            {
                rotation = q;
                return true;
            }
        }
        return false;
    }

    private bool MapsFacesOntoFaces(Quaternion q)
    {
        foreach (Vector3 n in localNormals)
        {
            Vector3 r = q * n;
            bool matched = false;
            foreach (Vector3 m in localNormals)
                if (Vector3.Dot(r, m) > 0.99f) { matched = true; break; }
            if (!matched) return false;
        }
        return true;
    }
}
