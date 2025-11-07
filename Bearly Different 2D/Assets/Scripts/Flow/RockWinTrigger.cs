using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using TMPro;  // ✅ Needed for TextMeshPro support

public class RockWinTrigger : MonoBehaviour
{
    [Header("Scene Transition")]
    public string nextSceneName = "4Page_Four";
    public float delayBeforeLoad = 5f;

    [Header("UI Message")]
    public TextMeshProUGUI winMessage;   // ✅ Assign your TMP text in the Inspector
    public string winText = "You reached the target!";

    private bool gameWon = false;

    public WinText wt;

    [Header("Rock Vanish (simple)")]
    public float rockFadeTime = 0.25f; // quarter-second fade

    void OnTriggerEnter2D(Collider2D other)
    {
        if (gameWon) return;

        if (other.CompareTag("Rock"))
        {
            gameWon = true;
            Debug.Log("Player won! Rocks landed.");

            // ✅ Disable spawner so no more rocks spawn
            BeehiveMiniGame3 spawner = FindObjectOfType<BeehiveMiniGame3>();
            if (spawner != null)
            {
                spawner.canSpawn = false;
            }

            var rocks = GameObject.FindGameObjectsWithTag("Rock");
            foreach (var r in rocks)
                StartCoroutine(FadeAndDestroy(r));

            // ✅ Display win message on screen
            if (winMessage != null)
            {
                winMessage.text = winText;
                winMessage.gameObject.SetActive(true);
            }

            wt.PlayWin();   

            // ✅ Load next scene after delay
            StartCoroutine(LoadNextSceneAfterDelay());
        }
    }

    IEnumerator LoadNextSceneAfterDelay()
    {
        yield return new WaitForSeconds(delayBeforeLoad);
        SceneManager.LoadScene(nextSceneName);
    }

    IEnumerator FadeAndDestroy(GameObject go)
    {
        if (go == null) yield break;

        // (optional) freeze physics/collisions so they stop moving during fade
        var rb = go.GetComponent<Rigidbody2D>();
        if (rb) rb.simulated = false;
        var col = go.GetComponent<Collider2D>();
        if (col) col.enabled = false;

        var sr = go.GetComponent<SpriteRenderer>();
        if (sr == null) { Destroy(go); yield break; }

        Color c = sr.color;
        Vector3 startScale = go.transform.localScale;
        float t = 0f, dur = Mathf.Max(0.01f, rockFadeTime);

        while (t < dur)
        {
            t += Time.deltaTime;
            float k = t / dur;

            // fade alpha
            c.a = 1f - k;
            sr.color = c;

            // (optional) tiny shrink for niceness
            go.transform.localScale = Vector3.Lerp(startScale, startScale * 0.85f, k);

            yield return null;
        }

        Destroy(go);
    }
}
