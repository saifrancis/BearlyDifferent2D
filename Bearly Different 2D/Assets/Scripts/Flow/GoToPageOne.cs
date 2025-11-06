using UnityEngine;
using UnityEngine.SceneManagement;

public class GoToPageOne : MonoBehaviour
{
    // You can change the target scene name from the Inspector if you like.
    [SerializeField] private string targetScene = "1Page_One";

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.S))
        {
            SceneManager.LoadScene(targetScene);
        }
    }
}
