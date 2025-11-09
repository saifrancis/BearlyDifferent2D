using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class CharacterMover : MonoBehaviour
{
    public bool moving;

    [Header("Path")]
    public Transform[] points;

    [Header("Motion")]
    [Min(0.01f)] public float speed = 3f;
    public bool loop = true;
    public bool pingPong = false;
    public float waitAtPoint = 0f;
    public AnimationCurve ease = AnimationCurve.Linear(0, 0, 1, 1);

    [Header("Start/Stop")]
    public bool playOnStart = true;

    [Header("Enable Toggle")]
    public bool isEnabled = true;

    [Header("Events")]
    public UnityEvent onPathComplete;

    int _dir = 1;
    int _i = 0;
    bool _running;

    [Header("Render + Animation")]
    public SpriteRenderer sr;
    public UnityEngine.UI.Image image;
    public Animator animator; // ✅ optional animator

    public bool playNextStart;
    public bool playNextEnd;
    public PanelManager panelManager;

    [Header("Shrink While Moving")]
    public bool enableShrink = false;
    public float shrinkSpeed = 1f;
    public float minScale = 0.1f;
    private bool _isActuallyMoving = false;

    void Start()
    {
        if (!moving) return;

        if (points == null || points.Length < 2)
        {
            Debug.LogWarning($"{nameof(CharacterMover)} needs at least 2 points.", this);
            enabled = false;
            return;
        }

        transform.position = points[0].position;

        if (playOnStart) StartMoving();
    }

    void Update()
    {
        // shrink only while actively moving and allowed
        if (enableShrink && _isActuallyMoving)
        {
            transform.localScale -= Vector3.one * shrinkSpeed * Time.deltaTime;
            if (transform.localScale.x < minScale)
                transform.localScale = Vector3.one * minScale;
        }

        // ✅ optional: keep Animator in sync
        if (animator != null)
            animator.SetBool("IsMoving", _isActuallyMoving);
    }

    public void StartMoving()
    {
        if (playNextStart) panelManager.ShowNextPanel();
        if (!moving) moving = true;
        if (_running) return;

        _running = true;
        StopAllCoroutines();
        StartCoroutine(MoveRoutine());
    }

    public void StopMoving()
    {
        _running = false;
        _isActuallyMoving = false;
        if (animator != null)
            animator.SetBool("IsMoving", false);
        StopAllCoroutines();
    }

    IEnumerator MoveRoutine()
    {
        while (true)
        {
            while (!isEnabled)
                yield return null;

            int next = _i + _dir;

            if (next < 0 || next >= points.Length)
            {
                if (pingPong)
                {
                    _dir *= -1;
                    next = _i + _dir;
                }
                else if (loop)
                {
                    next = 0;
                }
                else
                {
                    StopMoving();
                    onPathComplete?.Invoke();
                    yield break;
                }
            }

            Vector3 a = points[_i].position;
            Vector3 b = points[next].position;
            float dist = Vector3.Distance(a, b);

            float t = 0f;
            _isActuallyMoving = true;
            if (animator != null)
                animator.SetBool("IsMoving", true);

            while (t < 1f)
            {
                if (image != null && sr != null)
                    image.sprite = sr.sprite;

                float duration = Mathf.Max(0.0001f, dist / speed);
                t += Time.deltaTime / duration;
                float k = ease.Evaluate(Mathf.Clamp01(t));
                transform.position = Vector3.LerpUnclamped(a, b, k);
                yield return null;
            }

            _isActuallyMoving = false;
            if (animator != null)
                animator.SetBool("IsMoving", false);

            if (playNextEnd) panelManager.ShowNextPanel();

            _i = next;

            if (waitAtPoint > 0f)
                yield return new WaitForSeconds(waitAtPoint);
        }
    }

    void OnDrawGizmos()
    {
        if (points == null || points.Length < 2) return;

        Gizmos.color = new Color(1f, 0.6f, 0.2f, 1f);
        for (int i = 0; i < points.Length - 1; i++)
        {
            if (points[i] && points[i + 1])
            {
                Gizmos.DrawLine(points[i].position, points[i + 1].position);
                Gizmos.DrawWireSphere(points[i].position, 0.06f);
            }
        }
        if (points[^1]) Gizmos.DrawWireSphere(points[^1].position, 0.06f);
    }

    public void EnableShrink(bool on) => enableShrink = on;
}