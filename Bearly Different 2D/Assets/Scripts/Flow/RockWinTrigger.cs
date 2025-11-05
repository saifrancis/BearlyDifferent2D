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

            // ✅ Display win message on screen
            if (winMessage != null)
            {
                winMessage.text = winText;
                winMessage.gameObject.SetActive(true);
            }

            // ✅ Load next scene after delay
            StartCoroutine(LoadNextSceneAfterDelay());
        }
    }

    IEnumerator LoadNextSceneAfterDelay()
    {
        yield return new WaitForSeconds(delayBeforeLoad);
        SceneManager.LoadScene(nextSceneName);
    }
}
