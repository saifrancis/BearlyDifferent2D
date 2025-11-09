using UnityEngine;

public class TransitionTest : MonoBehaviour
{
    [Header("Scene names in Build Settings")]
    public string nextScene = "2Page_Two";
    public string prevScene = "3Page_Three";

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            TransitionLoader.Go(nextScene, fromRight: true, fps: 24);

        if (Input.GetKeyDown(KeyCode.Backspace))
            TransitionLoader.Go(prevScene, fromRight: false, fps: 24);
    }
}
