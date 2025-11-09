using UnityEngine;
using UnityEngine.SceneManagement;

public class GoToPageOne : MonoBehaviour
{
    [SerializeField] private string targetScene = "1Page_One";

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.S))
        {
            SceneManager.LoadScene(targetScene);
        }
    }
}
