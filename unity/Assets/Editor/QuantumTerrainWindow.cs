using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>Sliders that regenerate the terrain through python/terrain_server.py.</summary>
public class QuantumTerrainWindow : EditorWindow
{
    const string ServerUrl = "http://127.0.0.1:8765";
    const double SettleSeconds = 0.15;

    // Generation can take a while at higher resolutions, so the timeout is generous.
    static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

    int qubitsPerAxis = 8;
    int seed = 7;
    float beta = 5.5f;
    float entangle = 1f;
    float gamma = 0.5f;
    bool useShots;
    float shotsExponent = 5f;

    bool changed;
    double lastChangeTime;
    Task<TerrainResult> request;
    string status = "Move a slider to generate.";

    class TerrainResult
    {
        public byte[] Bytes;
        public string Entropy;
        public string Milliseconds;
        public string Error;
    }

    [MenuItem("Quantum Terrain/Live Controls")]
    static void Open()
    {
        GetWindow<QuantumTerrainWindow>("Quantum Terrain");
    }

    void OnEnable()
    {
        EditorApplication.update += Tick;
    }

    void OnDisable()
    {
        EditorApplication.update -= Tick;
    }

    void OnGUI()
    {
        EditorGUI.BeginChangeCheck();
        entangle = EditorGUILayout.Slider("Entanglement", entangle, 0f, 1f);
        beta = EditorGUILayout.Slider("Smoothness (beta)", beta, 2f, 8f);
        gamma = EditorGUILayout.Slider("Height curve (gamma)", gamma, 0.25f, 2f);
        seed = EditorGUILayout.IntField("Seed", seed);
        qubitsPerAxis = EditorGUILayout.IntSlider("Qubits per axis", qubitsPerAxis, 6, 9);

        useShots = EditorGUILayout.Toggle("Sample measurements", useShots);
        using (new EditorGUI.DisabledScope(!useShots))
        {
            shotsExponent = EditorGUILayout.Slider("Shots (10^n)", shotsExponent, 3f, 7f);
        }
        if (EditorGUI.EndChangeCheck())
        {
            changed = true;
            lastChangeTime = EditorApplication.timeSinceStartup;
        }

        EditorGUILayout.Space();
        int side = 1 << qubitsPerAxis;
        EditorGUILayout.LabelField("Map", $"{side}x{side}, {2 * qubitsPerAxis} qubits");
        EditorGUILayout.HelpBox(status, MessageType.None);
    }

    void Tick()
    {
        if (request != null)
        {
            if (!request.IsCompleted)
            {
                return;
            }
            FinishRequest(request);
            request = null;
            Repaint();
        }

        // Only the latest values matter, so changes made during a request wait
        // here and collapse into one follow-up request.
        bool settled = EditorApplication.timeSinceStartup - lastChangeTime >= SettleSeconds;
        if (changed && settled)
        {
            changed = false;
            status = "Generating...";
            request = Fetch(BuildUrl());
            Repaint();
        }
    }

    void FinishRequest(Task<TerrainResult> finished)
    {
        TerrainResult result = finished.Result;
        if (result.Error != null)
        {
            status = result.Error;
            return;
        }
        QuantumTerrainImporter.Apply(result.Bytes);
        status = $"Entanglement entropy: {result.Entropy} bits\nGenerated in {result.Milliseconds} ms";
    }

    string BuildUrl()
    {
        string url = $"{ServerUrl}/terrain?qubits_per_axis={qubitsPerAxis}&seed={seed}"
            + $"&beta={Format(beta)}&entangle={Format(entangle)}&gamma={Format(gamma)}";
        if (useShots)
        {
            url += $"&shots={Mathf.RoundToInt(Mathf.Pow(10f, shotsExponent))}";
        }
        return url;
    }

    static string Format(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    static async Task<TerrainResult> Fetch(string url)
    {
        try
        {
            using (HttpResponseMessage response = await Client.GetAsync(url))
            {
                if (!response.IsSuccessStatusCode)
                {
                    string message = await response.Content.ReadAsStringAsync();
                    return new TerrainResult { Error = $"Server error: {message}" };
                }
                return new TerrainResult
                {
                    Bytes = await response.Content.ReadAsByteArrayAsync(),
                    Entropy = response.Headers.GetValues("X-Entanglement-Entropy").First(),
                    Milliseconds = response.Headers.GetValues("X-Generation-Ms").First(),
                };
            }
        }
        catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException)
        {
            return new TerrainResult { Error = "Can't reach the server. Start python/terrain_server.py and try again." };
        }
    }
}
