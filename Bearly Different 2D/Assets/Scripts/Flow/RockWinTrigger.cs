using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class RockWinTrigger : MonoBehaviour
{
    public string nextSceneName = "4Page_Four";
    public float delayBeforeLoad = 5f;

    private bool gameWon = false;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (gameWon) return;

        if (other.CompareTag("Rock"))
        {
            gameWon = true;
            Debug.Log("Player won! Rocks landed.");

            BeehiveMiniGame3 spawner = FindObjectOfType<BeehiveMiniGame3>();
            if (spawner != null)
            {
                spawner.canSpawn = false;
            }

            StartCoroutine(LoadNextSceneAfterDelay());
        }
    }

    IEnumerator LoadNextSceneAfterDelay()
    {
        yield return new WaitForSeconds(delayBeforeLoad);
        SceneManager.LoadScene(nextSceneName);
    }
}
