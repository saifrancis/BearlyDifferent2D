using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class CharacterMover : MonoBehaviour
{
    public bool moving;
    [Header("Path")]
    public Transform[] points;

    [Header("Motion")]
    [Min(0.01f)] public float speed = 3f;     // units per second
    public bool loop = true;                  // loop back to the start
    public bool pingPong = false;             // bounce back and forth
    public float waitAtPoint = 0f;            // seconds to pause at each point
    public AnimationCurve ease = AnimationCurve.Linear(0, 0, 1, 1); // optional easing

    [Header("Start/Stop")]
    public bool playOnStart = true;

    [Header("Enable Toggle")]
    public bool isEnabled = true;


    [Header("Events")]
    public UnityEvent onPathComplete;

    int _dir = 1;          // forward (1) or backward (-1)
    int _i = 0;            // current target index
    bool _running;

    public SpriteRenderer sr;
    public UnityEngine.UI.Image image;

    public bool playNextStart;
    public bool playNextEnd;
    public PanelManager panelManager;
    void Start()
    {

        if (!moving) return;

        if (points == null || points.Length < 2)
        {
            Debug.LogWarning($"{nameof(CharacterMover)} needs at least 2 points.", this);
            enabled = false;
            return;
        }

        // Snap to first point at start
        transform.position = points[0].position;

        if (playOnStart) StartMoving();

    }

    public void StartMoving()
    {
        if (playNextStart) panelManager.ShowNextPanel();
        if (!moving) return;
        if (_running) return;
        _running = true;
        StopAllCoroutines();
        StartCoroutine(MoveRoutine());
    }

    public void StopMoving()
    {
        _running = false;
        StopAllCoroutines();
    }

    System.Collections.IEnumerator MoveRoutine()
    {
        while (true)
        {
            while (!isEnabled)
            {
                yield return null; // pause movement but don't break coroutine
            }

            int next = _i + _dir;

            // Handle end-of-path logic
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
                    onPathComplete?.Invoke();
                    _running = false;
                    yield break;
                }
            }

            // Travel from points[_i] to points[next] at constant speed with optional easing
            Vector3 a = points[_i].position;
            Vector3 b = points[next].position;
            float dist = Vector3.Distance(a, b);
            

            float t = 0f;
            while (t < 1f)
            {
                image.sprite = sr.sprite; 
                float duration = Mathf.Max(0.0001f, dist / speed);
                t += Time.deltaTime / duration;
                float k = ease.Evaluate(Mathf.Clamp01(t)); // apply easing to 0..1
                transform.position = Vector3.LerpUnclamped(a, b, k);
                yield return null;
            }

            if (playNextEnd) panelManager.ShowNextPanel();

            // Arrived at next point
            _i = next;

            // Optional wait
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
}
