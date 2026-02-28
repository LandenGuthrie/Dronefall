using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;

public class AudioManager : MonoBehaviour
{
    public const int INITIAL_POOL_SIZE = 10;
    public const string SAVE_FILE_NAME = "Audio Settings.json";
    
    public static AudioManager Instance { get; private set; }

    [Header("Configuration")]
    [SerializeField] private bool SelfInitialization = true; 
    [SerializeField] private AudioMixer MainMixer; 
    [SerializeField] private List<AudioDefinition> Audios;

    private void Awake()
    {
        // Singleton initialization
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        
        Instance = this;
        DontDestroyOnLoad(this.gameObject);

        LoadSettings();
    }
    private void Start()
    {
        if (SelfInitialization) InitializeAudioManager();
    }
    public void InitializeAudioManager()
    {
        foreach (var a in Audios.Where(a => !_audioDict.TryAdd(a.Name, a)))
        {
            Debug.LogWarning($"[AudioManager] Duplicate audio name found and ignored: '{a.Name}'");
        }

        for (var i = 0; i < INITIAL_POOL_SIZE; i++)
        {
            CreateNewAudioSource();
        }

        foreach (var a in Audios.Where(a => a.PlayOnStart && a.Type == AudioType.Track))
        {
            PlayTrack(a.Name);
        }
    }

    // --- CORE PLAYBACK ---
    public void PlayAudio(string audioName, int count = 1)
    {
        var audioDef = GetAudioDefinition(audioName);

        if (audioDef.Type == AudioType.Track)
        {
            PlayTrack(audioName);
            return;
        }

        if (Time.time - audioDef.LastPlayedTime < audioDef.SpamPreventionTime) return;
        audioDef.LastPlayedTime = Time.time;

        if (count <= 1)
        {
            var clip = GetRandomClip(audioDef);
            FireOneShot(audioDef, clip);
        }
        else
        {
            StartCoroutine(PlayAudioIterative(audioDef, count));
        }
    }
    
    public AudioSource PlayAttachedAudio(string audioName, Transform targetParent, float maxDistance = 50f, float minDistance = 1f)
    {
        var audioDef = GetAudioDefinition(audioName);
        var source = GetAvailableSource();
        var clip = GetRandomClip(audioDef);

        if (clip == null) return null;

        // Attach and reset position
        source.transform.SetParent(targetParent);
        source.transform.localPosition = Vector3.zero;

        // Setup 3D settings
        source.spatialBlend = 1f; // 1 means completely 3D (0 means 2D global)
        source.dopplerLevel = 0;
        // CHANGED: This forces a straight line from max volume to zero volume!
        source.rolloffMode = AudioRolloffMode.Linear; 
        
        // At this distance or closer, the sound is at 100% volume
        source.minDistance = minDistance; 
        
        // At this distance or further, the sound is at exactly 0% volume
        source.maxDistance = maxDistance;

        source.clip = clip;
        source.outputAudioMixerGroup = audioDef.MixerGroup;
        source.loop = audioDef.Loop;
        
        source.pitch = UnityEngine.Random.Range(audioDef.Pitch + audioDef.RandomPitchOffset.x, audioDef.Pitch + audioDef.RandomPitchOffset.y);
        source.volume = UnityEngine.Random.Range(audioDef.Volume + audioDef.RandomVolumeOffset.x, audioDef.Volume + audioDef.RandomVolumeOffset.y); 

        source.Play();

        // Return the source so the calling script (like your Drone) can call source.Stop() when it crashes
        return source;
    }
    public AudioSource PlayAtPosition(string audioName, Vector3 position, float maxDistance = 50f, float minDistance = 1f)
    {
        var audioDef = GetAudioDefinition(audioName);
        var source = GetAvailableSource();
        var clip = GetRandomClip(audioDef);

        if (clip == null) return null;

        // Attach and reset position
        source.transform.position = position;
        source.transform.localPosition = Vector3.zero;

        // Setup 3D settings
        source.spatialBlend = 1f; // 1 means completely 3D (0 means 2D global)
        source.dopplerLevel = 0;
        // CHANGED: This forces a straight line from max volume to zero volume!
        source.rolloffMode = AudioRolloffMode.Linear; 
        
        // At this distance or closer, the sound is at 100% volume
        source.minDistance = minDistance; 
        
        // At this distance or further, the sound is at exactly 0% volume
        source.maxDistance = maxDistance;

        source.clip = clip;
        source.outputAudioMixerGroup = audioDef.MixerGroup;
        source.loop = audioDef.Loop;
        
        source.pitch = UnityEngine.Random.Range(audioDef.Pitch + audioDef.RandomPitchOffset.x, audioDef.Pitch + audioDef.RandomPitchOffset.y);
        source.volume = UnityEngine.Random.Range(audioDef.Volume + audioDef.RandomVolumeOffset.x, audioDef.Volume + audioDef.RandomVolumeOffset.y); 

        source.Play();

        // Return the source so the calling script (like your Drone) can call source.Stop() when it crashes
        return source;
    }

    private void FireOneShot(AudioDefinition audioDef, AudioClip clipToPlay)
    {
        if (clipToPlay == null) return;

        var source = GetAvailableSource();
        ResetSourceTo2D(source); // Ensure it's not still acting as a 3D sound from a previous pool use
        
        source.clip = clipToPlay;
        source.outputAudioMixerGroup = audioDef.MixerGroup;
        source.loop = false;
        
        source.pitch = UnityEngine.Random.Range(audioDef.Pitch + audioDef.RandomPitchOffset.x, audioDef.Pitch + audioDef.RandomPitchOffset.y);
        source.volume = UnityEngine.Random.Range(audioDef.Volume + audioDef.RandomVolumeOffset.x, audioDef.Volume + audioDef.RandomVolumeOffset.y); 

        source.Play();
    }
    
    private IEnumerator PlayAudioIterative(AudioDefinition audioDef, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var clip = GetRandomClip(audioDef);
            if (clip == null) yield break;

            FireOneShot(audioDef, clip);
            yield return new WaitForSeconds(clip.length);
        }
    }

    // --- TRACK MANAGEMENT ---
    private void PlayTrack(string audioName)
    {
        if (_activeTracks.ContainsKey(audioName) && _activeTracks[audioName].isPlaying) return;

        var audioDef = GetAudioDefinition(audioName);
        var source = GetAvailableSource();
        var clip = GetRandomClip(audioDef);

        if (clip == null) return;

        ResetSourceTo2D(source);

        source.clip = clip;
        source.outputAudioMixerGroup = audioDef.MixerGroup;
        source.loop = audioDef.Loop;
        source.pitch = audioDef.Pitch; 
        source.volume = audioDef.Volume; 

        source.Play();

        if (!_activeTracks.ContainsKey(audioName))
            _activeTracks.Add(audioName, source);
        else
            _activeTracks[audioName] = source;
    }
    public void PauseTrack(string audioName)
    {
        if (_activeTracks.TryGetValue(audioName, out var source)) source.Pause();
    }
    public void ResumeTrack(string audioName)
    {
        if (_activeTracks.TryGetValue(audioName, out var source)) source.UnPause();
    }
    public void StopTrack(string audioName)
    {
        if (_activeTracks.TryGetValue(audioName, out var source))
        {
            if (source != null) source.Stop();
            _activeTracks.Remove(audioName); 
        }
    }

    // --- POOLING SYSTEM ---
    private AudioSource GetAvailableSource()
    {
        // Safety check: If a drone was destroyed while holding an AudioSource, the source gets destroyed too.
        // This removes null references from our pool so we don't get errors.
        _sourcePool.RemoveAll(source => source == null);

        foreach (var source in _sourcePool)
        {
            if (!source.isPlaying) return source;
        }
        
        return CreateNewAudioSource();
    }
    
    private AudioSource CreateNewAudioSource()
    {
        GameObject go = new GameObject($"AudioSource_Pool_{_sourcePool.Count}");
        go.transform.SetParent(this.transform);
        AudioSource newSource = go.AddComponent<AudioSource>();
        newSource.playOnAwake = false;
        _sourcePool.Add(newSource);
        return newSource;
    }

    private void ResetSourceTo2D(AudioSource source)
    {
        // Brings the source back to the manager and resets it to 2D
        if (source.transform.parent != this.transform)
        {
            source.transform.SetParent(this.transform);
            source.transform.localPosition = Vector3.zero;
        }
        source.spatialBlend = 0f;
    }

    // --- SAVING/LOADING ---
    public void SaveSettings()
    {
        var json = JsonUtility.ToJson(_saveData, true);
        var path = Path.Combine(Application.persistentDataPath, SAVE_FILE_NAME);
        
        try
        {
            File.WriteAllText(path, json);
            Debug.Log($"[AudioManager] Settings saved successfully to {path}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[AudioManager] Failed to save settings: {e.Message}");
        }
    }
    public void LoadSettings()
    {
        var path = Path.Combine(Application.persistentDataPath, SAVE_FILE_NAME);

        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                JsonUtility.FromJsonOverwrite(json, _saveData);
                
                for (var i = 0; i < _saveData.MixerParameters.Count; i++)
                {
                    SetMixerVolume(_saveData.MixerParameters[i], _saveData.MixerVolumes[i], new Vector2(-80f, 0f));
                }
                
                Debug.Log("[AudioManager] Settings loaded successfully.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AudioManager] Failed to load settings: {e.Message}");
            }
        }
        else
        {
            Debug.Log("[AudioManager] No save file found, using default audio settings.");
        }
    }
    
    // --- UTILITIES ---
    private AudioClip GetRandomClip(AudioDefinition def)
    {
        if (def.SoundClips == null || def.SoundClips.Count == 0) return null;
        if (def.SoundClips.Count == 1) return def.SoundClips[0];

        int randomIndex = UnityEngine.Random.Range(0, def.SoundClips.Count);

        if (def.PreventRepeat)
        {
            while (randomIndex == def.LastPlayedIndex)
            {
                randomIndex = UnityEngine.Random.Range(0, def.SoundClips.Count);
            }
        }

        def.LastPlayedIndex = randomIndex;
        return def.SoundClips[randomIndex];
    }

    private void UpdateSaveDataMemory(string paramName, float normalizedVolume)
    {
        var index = _saveData.MixerParameters.IndexOf(paramName);
        if (index >= 0)
        {
            _saveData.MixerVolumes[index] = normalizedVolume;
        }
        else
        {
            _saveData.MixerParameters.Add(paramName);
            _saveData.MixerVolumes.Add(normalizedVolume);
        }
    }
    public float GetMixerVolume(string exposedParamName)
    {
        if (MainMixer == null)
        {
            Debug.LogError("[AudioManager] Main AudioMixer is not assigned!");
            return -1;
        }
        MainMixer.GetFloat(exposedParamName, out var value);
        return value;
    }
    public void SetMixerVolume(string exposedParamName, float normalizedVolume, Vector2 minMaxDb)
    {
        if (MainMixer == null)
        {
            Debug.LogError("[AudioManager] Main AudioMixer is not assigned!");
            return;
        }
        var dbVolume = ConvertRange(normalizedVolume, new Vector2(0, 1), minMaxDb);
        MainMixer.SetFloat(exposedParamName, dbVolume);
        
        UpdateSaveDataMemory(exposedParamName, normalizedVolume);
    }
    public AudioDefinition GetAudioDefinition(string audioName)
    {
        if (string.IsNullOrEmpty(audioName)) throw new ArgumentNullException(nameof(audioName));
        
        if (_audioDict.TryGetValue(audioName, out var audioDef))
        {
            return audioDef;
        }
        throw new Exception($"[AudioManager] No audio found with the name '{audioName}'");
    }
    public static float ConvertRange(float value, Vector2 fromMinMax, Vector2 toMinMax)
    {
        var fromMin = fromMinMax.x;
        var fromMax = fromMinMax.y;
        var toMin = toMinMax.x;
        var toMax = toMinMax.y;

        if (Mathf.Approximately(fromMin, fromMax)) return toMin;

        var normalized = (value - fromMin) / (fromMax - fromMin);
        return toMin + normalized * (toMax - toMin);
    }

    private readonly Dictionary<string, AudioDefinition> _audioDict = new Dictionary<string, AudioDefinition>();
    private readonly List<AudioSource> _sourcePool = new List<AudioSource>();
    private readonly Dictionary<string, AudioSource> _activeTracks = new Dictionary<string, AudioSource>();
    private readonly AudioSettingsSaveData _saveData = new();
}

public class AudioSettingsSaveData
{
    public readonly List<string> MixerParameters = new();
    public readonly List<float> MixerVolumes = new();
}

[Serializable]
public class AudioDefinition
{
    [Header("Common Audio Settings")]
    public string Name = "Audio";
    public AudioType Type = AudioType.OneShot;
    public float Pitch;
    public float Volume;
    public List<AudioClip> SoundClips = new List<AudioClip>(); // List integration
    public AudioMixerGroup MixerGroup; 

    [Header("One Shot Audio Settings")] 
    public Vector2 RandomVolumeOffset = new Vector2(-0.1f, 0.1f); 
    public Vector2 RandomPitchOffset = new Vector2(-0.1f, 0.1f);
    public float SpamPreventionTime = 0.15f;
    public bool PreventRepeat = true; 
    
    internal float LastPlayedTime;
    internal int LastPlayedIndex = -1;
    
    [Header("Track Settings")]
    public bool PlayOnStart;
    public bool Loop;
}

public enum AudioType
{
    OneShot,
    Track,
}