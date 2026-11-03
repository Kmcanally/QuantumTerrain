using System;
using UnityEngine;

/// <summary>
/// Orbits the camera around the terrain in Play mode and shows what the quantum
/// circuit is doing. The Live Controls window fills in the readout.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class QuantumTerrainPresentation : MonoBehaviour
{
    [SerializeField] float secondsPerOrbit = 90f;
    [SerializeField] float distance = 0.85f;
    [SerializeField] float elevationDegrees = 28f;

    [NonSerialized] public bool HasReading;
    [NonSerialized] public float Entangle;
    [NonSerialized] public int QubitsPerAxis;
    [NonSerialized] public long? Shots;
    [NonSerialized] public string Entropy;

    Terrain terrain;
    GUIStyle titleStyle;
    GUIStyle textStyle;
    GUIStyle captionStyle;
    int styledForHeight;

    void Start()
    {
        terrain = FindObjectOfType<Terrain>();
        if (terrain != null)
        {
            GetComponent<Camera>().farClipPlane = Mathf.Max(GetComponent<Camera>().farClipPlane, terrain.terrainData.size.x * 3f);
        }
    }

    void LateUpdate()
    {
        if (!Application.isPlaying || terrain == null)
        {
            return;
        }
        Vector3 size = terrain.terrainData.size;
        Vector3 center = terrain.transform.position + new Vector3(size.x / 2f, size.y / 3f, size.z / 2f);
        float angle = Time.time / secondsPerOrbit * 360f;
        Vector3 offset = Quaternion.Euler(elevationDegrees, angle, 0f) * Vector3.back * (size.x * distance);
        transform.position = center + offset;
        transform.LookAt(center);
    }

    void OnGUI()
    {
        if (!HasReading)
        {
            return;
        }
        UpdateStyles();

        float unit = Screen.height / 40f;
        var panel = new Rect(unit, Screen.height - unit * 11f, unit * 22f, unit * 10f);
        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = previous;

        GUILayout.BeginArea(new Rect(panel.x + unit, panel.y + unit * 0.6f, panel.width - unit * 2f, panel.height - unit));
        GUILayout.Label("Quantum Terrain", titleStyle);
        int side = 1 << QubitsPerAxis;
        GUILayout.Label($"Entanglement  {Entangle:0.00}", textStyle);
        GUILayout.Label($"{2 * QubitsPerAxis} qubits  ({side} x {side} map)", textStyle);
        GUILayout.Label(Shots.HasValue ? $"{Shots.Value:N0} measurements" : "Exact probabilities", textStyle);
        GUILayout.Label($"Entanglement entropy  {Entropy} bits", textStyle);
        GUILayout.Space(unit * 0.4f);
        GUILayout.Label(Caption(), captionStyle);
        GUILayout.EndArea();
    }

    string Caption()
    {
        if (Shots.HasValue)
        {
            return "Each measurement lands on one point of the map. More measurements sharpen the terrain.";
        }
        if (Entangle < 0.05f)
        {
            return "No entangling gates: only single-qubit phases, so the map stays mirrored and aligned to the axes.";
        }
        if (Entangle < 0.5f)
        {
            return "Controlled-phase gates entangle the qubits and start breaking the symmetry.";
        }
        return "Interference between thousands of frequencies forms natural landforms.";
    }

    void UpdateStyles()
    {
        if (titleStyle != null && styledForHeight == Screen.height)
        {
            return;
        }
        styledForHeight = Screen.height;
        int textSize = Mathf.RoundToInt(Screen.height / 45f);
        textStyle = new GUIStyle(GUI.skin.label) { fontSize = textSize };
        textStyle.normal.textColor = Color.white;
        titleStyle = new GUIStyle(textStyle) { fontSize = Mathf.RoundToInt(textSize * 1.4f), fontStyle = FontStyle.Bold };
        captionStyle = new GUIStyle(textStyle) { wordWrap = true, fontStyle = FontStyle.Italic };
        captionStyle.normal.textColor = new Color(0.85f, 0.9f, 1f);
    }
}
