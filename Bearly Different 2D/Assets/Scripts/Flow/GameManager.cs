using UnityEngine;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public GridManager gridManager;

    private int currentRow = 0;
    private int currentCol = 0;
    private Berry activeBerry;

    private bool isChoosingSwap = false;
    private int successfulMatches = 0;
    public int matchesNeeded = 5;

    private bool levelComplete = false; // prevents further input

    void Start()
    {
        SetActiveBerry(gridManager.grid[currentRow, currentCol]);
    }

    void Update()
    {
        // Stop processing input once the level is complete
        if (levelComplete) return;

        if (!isChoosingSwap)
        {
            HandleNavigation();
            if (Input.GetKeyDown(KeyCode.Space))
            {
                isChoosingSwap = true;
            }
        }
        else
        {
            HandleSwap();
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SceneManager.LoadScene("1Page_One");
        }
    }

    void HandleNavigation()
    {
        if (Input.GetKeyDown(KeyCode.RightArrow) && currentCol < gridManager.cols - 1)
            MoveTo(currentRow, currentCol + 1);
        else if (Input.GetKeyDown(KeyCode.LeftArrow) && currentCol > 0)
            MoveTo(currentRow, currentCol - 1);
        else if (Input.GetKeyDown(KeyCode.UpArrow) && currentRow > 0)
            MoveTo(currentRow - 1, currentCol);
        else if (Input.GetKeyDown(KeyCode.DownArrow) && currentRow < gridManager.rows - 1)
            MoveTo(currentRow + 1, currentCol);
    }

    void HandleSwap()
    {
        int targetRow = currentRow;
        int targetCol = currentCol;

        if (Input.GetKeyDown(KeyCode.RightArrow)) targetCol++;
        else if (Input.GetKeyDown(KeyCode.LeftArrow)) targetCol--;
        else if (Input.GetKeyDown(KeyCode.UpArrow)) targetRow--;
        else if (Input.GetKeyDown(KeyCode.DownArrow)) targetRow++;
        else return;

        if (targetRow >= 0 && targetRow < gridManager.rows && targetCol >= 0 && targetCol < gridManager.cols)
        {
            Berry targetBerry = gridManager.grid[targetRow, targetCol];
            gridManager.Swap(activeBerry, targetBerry);

            List<Berry> matches = gridManager.FindMatches();
            if (matches.Count > 0)
            {
                successfulMatches++;
                Debug.Log("Matches: " + successfulMatches);

                gridManager.RemoveAndCollapse(matches);

                if (successfulMatches >= matchesNeeded)
                {
                    Debug.Log("Level Complete!");
                    levelComplete = true; // stop input
                    StartCoroutine(LoadNextSceneAfterDelay(5f));
                }
            }
        }

        isChoosingSwap = false;
    }

    System.Collections.IEnumerator LoadNextSceneAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SceneManager.LoadScene("3Page_Two");
    }

    void MoveTo(int newRow, int newCol)
    {
        activeBerry.Highlight(false);
        currentRow = newRow;
        currentCol = newCol;
        SetActiveBerry(gridManager.grid[currentRow, currentCol]);
    }

    void SetActiveBerry(Berry berry)
    {
        activeBerry = berry;
        activeBerry.Highlight(true);
    }
}