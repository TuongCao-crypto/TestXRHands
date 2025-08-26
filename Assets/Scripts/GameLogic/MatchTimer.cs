using UnityEngine;
using TMPro;
using UnityEngine.Events;

/// <summary>
/// Simple match timer: counts down from startSeconds to 0 and fires events.
/// Attach to a GameObject, optionally assign a TMP_Text to display time.
/// </summary>
public class MatchTimer : MonoBehaviour
{
    [System.Serializable] public class FloatEvent : UnityEvent<float> { }

    [Header("Config")]
    [Min(0)] public float startSeconds = 60f;    // initial duration (default 60s)
    public bool useUnscaledTime = false;         // use unscaled delta time (ignores Time.timeScale)

    public TMP_Text display;                     // TextMeshPro or TextMeshProUGUI
    public bool showTenths = false;              // show tenths (MM:SS.s) instead of whole seconds

    [Header("Events")]
    public UnityEvent onTimerStart;              // invoked when timer starts
    public UnityEvent onTimerPause;              // invoked when paused
    public UnityEvent onTimerResume;             // invoked when resumed
    public UnityEvent onTimerEnd;                // invoked when reaching 0
    public FloatEvent onTick;                    // invoked every frame with remaining seconds

    public float RemainingSeconds => _remaining; // current remaining time
    public bool IsRunning { get; private set; }  // is the timer currently running?

    private float _remaining;

    private void Awake()
    {
        _remaining = Mathf.Max(0f, startSeconds);
        UpdateUI();
    }

    /// <summary>Start (or restart) the timer from startSeconds.</summary>
    public void StartTimer()
    {
        _remaining = Mathf.Max(0f, startSeconds);
        IsRunning = true;
        onTimerStart?.Invoke();
        UpdateUI();
    }

    /// <summary>Pause the timer (keeps remaining time).</summary>
    public void PauseTimer()
    {
        if (!IsRunning) return;
        IsRunning = false;
        onTimerPause?.Invoke();
    }

    /// <summary>Resume the timer if not finished.</summary>
    public void ResumeTimer()
    {
        if (IsRunning || _remaining <= 0f) return;
        IsRunning = true;
        onTimerResume?.Invoke();
    }

    /// <summary>Reset without starting. Optionally override startSeconds.</summary>
    public void ResetTimer(float newStartSeconds = -1f)
    {
        if (newStartSeconds >= 0f) startSeconds = newStartSeconds;
        _remaining = Mathf.Max(0f, startSeconds);
        IsRunning = false;
        UpdateUI();
    }

    /// <summary>Add (or subtract) time at runtime.</summary>
    public void AddTime(float seconds)
    {
        _remaining = Mathf.Max(0f, _remaining + seconds);
        UpdateUI();
    }

    private void Update()
    {
        if (!IsRunning) return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (dt <= 0f) return;

        _remaining -= dt;

        if (_remaining <= 0f)
        {
            _remaining = 0f;
            IsRunning = false;
            UpdateUI();
            onTick?.Invoke(_remaining);
            onTimerEnd?.Invoke();
            return;
        }

        onTick?.Invoke(_remaining);
        UpdateUI();
    }

    private void UpdateUI()
    {
        if (!display) return;

        if (showTenths)
        {
            int minutes = Mathf.FloorToInt(_remaining / 60f);
            float seconds = _remaining - minutes * 60f;
            display.text = $"{minutes:00}:{seconds:00.0}";
        }
        else
        {
            int minutes = Mathf.FloorToInt(_remaining / 60f);
            int seconds = Mathf.CeilToInt(_remaining - minutes * 60f);
            if (seconds == 60) { minutes += 1; seconds = 0; } // rounding edge-case
            display.text = $"{minutes:00}:{seconds:00}";
        }
    }
}