using UnityEngine;
using TMPro;
using UnityEngine.Events;

public class ScoreManager : SingletonMono<ScoreManager>
{
    [System.Serializable] public class ScoreChangedEvent : UnityEvent<int, int> { }   // (attacker, defense)
    [System.Serializable] public class WinnerEvent : UnityEvent<string> { }           // "Attacker", "Defense", "Draw"

    public enum Team { Attacker, Defense }

    [Header("Points")]
    [Min(0)] public int pointsPerDataCapture = 50;
    [Min(0)] public int pointsPerDroneDown   = 50;

    [Header("Optional Timer Hook")]
    public MatchTimer matchTimer;          // If assigned, winner is decided on timer end

    [Header("Optional UI")]
    public TMP_Text attackerScoreText;
    public TMP_Text defenseScoreText;
    public TMP_Text resultText;            // Shows winner text on match end

    [Header("Events")]
    public ScoreChangedEvent onScoreChanged;
    public WinnerEvent onWinnerDecided;

    public int AttackerScore => _attackerScore;
    public int DefenseScore  => _defenseScore;
    public bool MatchEnded   => _ended;

    private int _attackerScore;
    private int _defenseScore;
    private bool _ended;

    private void Awake()
    {
        StartMatch();
    }

    private void OnEnable()
    {
        // Auto-subscribe to timer end if provided
        if (matchTimer != null)
            matchTimer.onTimerEnd.AddListener(EndMatchNow);
    }

    private void OnDisable()
    {
        if (matchTimer != null)
            matchTimer.onTimerEnd.RemoveListener(EndMatchNow);
    }

    public void StartMatch()
    {
        ResetScores();
        matchTimer.StartTimer();
    }

    /// <summary>
    /// Award points to the Attacker team for a successful data capture.
    /// </summary>
    public void RegisterDataCaptured()
    {
        if (_ended) return;
        _attackerScore += pointsPerDataCapture;
        UpdateUIAndNotify();
    }

    /// <summary>
    /// Award points to the Defense team for taking down a drone.
    /// </summary>
    public void RegisterDroneDowned()
    {
        if (_ended) return;
        _defenseScore += pointsPerDroneDown;
        UpdateUIAndNotify();
    }
    
    public void RegisterDroneTakeDamge()
    {
        if (_ended) return;
        _defenseScore += 10;
        UpdateUIAndNotify();
    }

    /// <summary>
    /// Decides the winner immediately (used when the timer ends or forced).
    /// </summary>
    public void EndMatchNow()
    {
        if (_ended) return;
        _ended = true;

        string winner;
        if (_attackerScore > _defenseScore)      winner = "Attacker";
        else if (_defenseScore > _attackerScore) winner = "Defense";
        else                                      winner = "Draw";

        if (resultText)
        {
            resultText.text = winner == "Draw" ? "DRAW" : $"{winner.ToUpper()} WINS!";
            resultText.gameObject.SetActive(true);
        }

        onWinnerDecided?.Invoke(winner);
        matchTimer.PauseTimer();

        GameManager.Instance.EndGame();
        
    }

    /// <summary>
    /// Resets scores to 0 and clears UI/result state.
    /// </summary>
    public void ResetScores()
    {
        _attackerScore = 0;
        _defenseScore  = 0;
        _ended = false;
        UpdateUIAndNotify();
        if (resultText) { resultText.text = ""; resultText.gameObject.SetActive(false); }
    }

    private void UpdateUIAndNotify()
    {
        if (attackerScoreText) attackerScoreText.text = _attackerScore.ToString();
        if (defenseScoreText)  defenseScoreText.text  = _defenseScore.ToString();
        onScoreChanged?.Invoke(_attackerScore, _defenseScore);
    }
}
