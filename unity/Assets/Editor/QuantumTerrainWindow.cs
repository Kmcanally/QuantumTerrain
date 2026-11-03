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
    const double SweepSeconds = 10.0;

    // Longer than the gap between server results, so a sweep never pauses on one.
    const double GlideSeconds = 0.35;
    const double GlideStepSeconds = 1.0 / 30.0;
    const float MinShotsExponent = 3f;
    const float MaxShotsExponent = 9f;
    const int PresetsPerRow = 3;

    // Generation can take a while at higher resolutions, so the timeout is generous.
    static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

    // Serialized so the settings survive the code reload when entering Play mode.
    [SerializeField] int qubitsPerAxis = 8;
    [SerializeField] int seed = 7;
    [SerializeField] float beta = 5.5f;
    [SerializeField] float detail = 0.75f;
    [SerializeField] float entangle = 1f;
    [SerializeField] float gamma = 0.5f;
    [SerializeField] bool useShots;
    [SerializeField] float shotsExponent = 6f;

    bool changed;
    double lastChangeTime;
    Task<TerrainResult> request;
    string status = "Move a slider to generate.";
    Sweep sweep = Sweep.None;
    double sweepStartTime;

    float[,] shownHeights;
    float[,] glideFrom;
    float[,] glideTo;
    double glideStartTime;
    double lastGlideStepTime;

    enum Sweep { None, Entanglement, Shots }

    static readonly Preset[] Presets =
    {
        new Preset("Mirrored Grid", 0f, 5.5f, 0.75f, 0.5f, 3),
        new Preset("Symmetry Breaking", 0.25f, 5.5f, 0.75f, 0.5f, 3),
        new Preset("Alpine Range", 1f, 5.5f, 0.75f, 0.5f, 7),
        new Preset("Rolling Hills", 1f, 7f, 0.25f, 0.5f, 12),
        new Preset("Rugged Peaks", 1f, 4.5f, 0f, 0.5f, 12),
        new Preset("Measurement Noise", 1f, 5.5f, 0f, 0.5f, 7, 7f),
    };

    class Preset
    {
        public readonly string Name;
        public readonly float Entangle;
        public readonly float Beta;
        public readonly float Detail;
        public readonly float Gamma;
        public readonly int Seed;
        public readonly float? ShotsExponent;

        public Preset(string name, float entangle, float beta, float detail, float gamma, int seed, float? shotsExponent = null)
        {
            Name = name;
            Entangle = entangle;
            Beta = beta;
            Detail = detail;
            Gamma = gamma;
            Seed = seed;
            ShotsExponent = shotsExponent;
        }
    }

    class TerrainResult
    {
        public float Entangle;
        public int QubitsPerAxis;
        public long? Shots;
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
        detail = EditorGUILayout.Slider("Fine detail", detail, 0f, 1f);
        gamma = EditorGUILayout.Slider("Height curve (gamma)", gamma, 0.25f, 2f);
        seed = EditorGUILayout.IntField("Seed", seed);
        qubitsPerAxis = EditorGUILayout.IntSlider("Qubits per axis", qubitsPerAxis, 6, 9);

        useShots = EditorGUILayout.Toggle("Sample measurements", useShots);
        using (new EditorGUI.DisabledScope(!useShots))
        {
            shotsExponent = EditorGUILayout.Slider("Shots (10^n)", shotsExponent, MinShotsExponent, MaxShotsExponent);
        }
        if (EditorGUI.EndChangeCheck())
        {
            sweep = Sweep.None;
            MarkChanged();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Sweeps", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawSweepButton(Sweep.Entanglement, "Sweep Entanglement");
            DrawSweepButton(Sweep.Shots, "Sweep Measurements");
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Presets", EditorStyles.boldLabel);
        DrawPresets();

        EditorGUILayout.Space();
        int side = 1 << qubitsPerAxis;
        EditorGUILayout.LabelField("Map", $"{side}x{side}, {2 * qubitsPerAxis} qubits");
        EditorGUILayout.HelpBox(status, MessageType.None);
    }

    void DrawSweepButton(Sweep target, string label)
    {
        if (!GUILayout.Button(sweep == target ? "Stop" : label))
        {
            return;
        }
        if (sweep == target)
        {
            sweep = Sweep.None;
            return;
        }
        sweep = target;
        sweepStartTime = EditorApplication.timeSinceStartup;
        useShots = target == Sweep.Shots;
        GUI.FocusControl(null);
    }

    void DrawPresets()
    {
        for (int i = 0; i < Presets.Length; i += PresetsPerRow)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int j = i; j < Mathf.Min(i + PresetsPerRow, Presets.Length); j++)
                {
                    if (GUILayout.Button(Presets[j].Name))
                    {
                        ApplyPreset(Presets[j]);
                    }
                }
            }
        }
    }

    void ApplyPreset(Preset preset)
    {
        sweep = Sweep.None;
        entangle = preset.Entangle;
        beta = preset.Beta;
        detail = preset.Detail;
        gamma = preset.Gamma;
        seed = preset.Seed;
        useShots = preset.ShotsExponent.HasValue;
        shotsExponent = preset.ShotsExponent ?? shotsExponent;
        GUI.FocusControl(null);
        MarkChanged();
    }

    void MarkChanged()
    {
        changed = true;
        lastChangeTime = EditorApplication.timeSinceStartup;
    }

    void AdvanceSweep()
    {
        float progress = Mathf.Clamp01((float)((EditorApplication.timeSinceStartup - sweepStartTime) / SweepSeconds));
        if (sweep == Sweep.Entanglement)
        {
            entangle = progress;
        }
        else
        {
            shotsExponent = Mathf.Lerp(MinShotsExponent, MaxShotsExponent, progress);
        }
        changed = true;
        if (progress >= 1f)
        {
            sweep = Sweep.None;
        }
        Repaint();
    }

    /// <summary>Glides from whatever is on screen to new heights, or snaps if the size changed.</summary>
    void GlideTo(float[,] heights)
    {
        if (shownHeights == null || shownHeights.Length != heights.Length)
        {
            shownHeights = heights;
            glideTo = null;
            QuantumTerrainImporter.Apply(heights);
            SceneView.RepaintAll();
            return;
        }
        glideFrom = (float[,])shownHeights.Clone();
        glideTo = heights;
        glideStartTime = EditorApplication.timeSinceStartup;
    }

    void StepGlide()
    {
        double now = EditorApplication.timeSinceStartup;
        if (glideTo == null || now - lastGlideStepTime < GlideStepSeconds)
        {
            return;
        }
        lastGlideStepTime = now;

        float progress = Mathf.Clamp01((float)((now - glideStartTime) / GlideSeconds));
        if (progress >= 1f)
        {
            shownHeights = glideTo;
            glideTo = null;
            QuantumTerrainImporter.Apply(shownHeights);
        }
        else
        {
            int size = shownHeights.GetLength(0);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    shownHeights[y, x] = Mathf.Lerp(glideFrom[y, x], glideTo[y, x], progress);
                }
            }
            QuantumTerrainImporter.Apply(shownHeights, preview: true);
        }
        SceneView.RepaintAll();
    }

    void Tick()
    {
        StepGlide();

        bool sweeping = sweep != Sweep.None;
        if (sweeping)
        {
            AdvanceSweep();
        }

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
        if (changed && (settled || sweeping))
        {
            changed = false;
            status = "Generating...";
            request = Fetch(BuildUrl(), entangle, qubitsPerAxis, RequestedShots());
            Repaint();
        }
    }

    void FinishRequest(Task<TerrainResult> finished)
    {
        TerrainResult result = finished.Result;
        if (result.Error != null)
        {
            sweep = Sweep.None;
            status = result.Error;
            return;
        }
        GlideTo(QuantumTerrainImporter.Decode(result.Bytes));
        status = $"Entanglement entropy: {result.Entropy} bits\nGenerated in {result.Milliseconds} ms";

        var presentation = FindObjectOfType<QuantumTerrainPresentation>();
        if (presentation != null)
        {
            presentation.HasReading = true;
            presentation.Entangle = result.Entangle;
            presentation.QubitsPerAxis = result.QubitsPerAxis;
            presentation.Shots = result.Shots;
            presentation.Entropy = result.Entropy;
        }
    }

    long? RequestedShots()
    {
        return useShots ? (long)Math.Pow(10.0, shotsExponent) : (long?)null;
    }

    string BuildUrl()
    {
        string url = $"{ServerUrl}/terrain?qubits_per_axis={qubitsPerAxis}&seed={seed}"
            + $"&beta={Format(beta)}&detail={Format(detail)}&entangle={Format(entangle)}&gamma={Format(gamma)}";
        if (useShots)
        {
            url += $"&shots={RequestedShots()}";
        }
        return url;
    }

    static string Format(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    static async Task<TerrainResult> Fetch(string url, float entangle, int qubitsPerAxis, long? shots)
    {
        TerrainResult result = await Download(url);
        result.Entangle = entangle;
        result.QubitsPerAxis = qubitsPerAxis;
        result.Shots = shots;
        return result;
    }

    static async Task<TerrainResult> Download(string url)
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
