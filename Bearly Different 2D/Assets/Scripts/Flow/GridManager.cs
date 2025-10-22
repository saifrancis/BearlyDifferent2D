using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class GridManager : MonoBehaviour
{
    public GameObject berryPrefab;
    public Transform gridPanel;
    public Sprite[] berrySprites;

    public int rows = 8;
    public int cols = 8;
    public Berry[,] grid;

    void Start()
    {
        GenerateGrid();
    }

    public void GenerateGrid()
    {
        grid = new Berry[rows, cols];

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                GameObject obj = Instantiate(berryPrefab, gridPanel);
                Berry berry = obj.GetComponent<Berry>();

                int randomType = GetRandomType(r, c);
                berry.SetType(randomType, berrySprites[randomType]);

                berry.row = r;
                berry.col = c;
                grid[r, c] = berry;
            }
        }
    }

    int GetRandomType(int row, int col)
    {
        int type;
        bool hasMatch;

        do
        {
            type = Random.Range(0, berrySprites.Length);
            hasMatch = false;

            if (col >= 2 &&
                grid[row, col - 1].type == type &&
                grid[row, col - 2].type == type)
                hasMatch = true;

            if (row >= 2 &&
                grid[row - 1, col].type == type &&
                grid[row - 2, col].type == type)
                hasMatch = true;

        } while (hasMatch);

        return type;
    }

    public void Swap(Berry a, Berry b)
    {
        int tempType = a.type;
        Sprite tempSprite = a.GetComponent<Image>().sprite;

        a.SetType(b.type, b.GetComponent<Image>().sprite);
        b.SetType(tempType, tempSprite);
    }

    public List<Berry> FindMatches()
    {
        List<Berry> matches = new List<Berry>();

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols - 2; c++)
            {
                int type = grid[r, c].type;
                if (type == -1) continue;

                if (grid[r, c + 1].type == type && grid[r, c + 2].type == type)
                {
                    matches.Add(grid[r, c]);
                    matches.Add(grid[r, c + 1]);
                    matches.Add(grid[r, c + 2]);
                }
            }
        }

        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows - 2; r++)
            {
                int type = grid[r, c].type;
                if (type == -1) continue;

                if (grid[r + 1, c].type == type && grid[r + 2, c].type == type)
                {
                    matches.Add(grid[r, c]);
                    matches.Add(grid[r + 1, c]);
                    matches.Add(grid[r + 2, c]);
                }
            }
        }

        return matches;
    }

    public void RemoveAndCollapse(List<Berry> matches)
    {
        foreach (Berry b in matches)
        {
            b.SetType(-1, null);
        }

        for (int c = 0; c < cols; c++)
        {
            int emptyCount = 0;
            for (int r = rows - 1; r >= 0; r--)
            {
                if (grid[r, c].type == -1)
                {
                    emptyCount++;
                }
                else if (emptyCount > 0)
                {
                    Berry current = grid[r, c];
                    Berry target = grid[r + emptyCount, c];

                    target.SetType(current.type, current.GetComponent<Image>().sprite);
                    current.SetType(-1, null);
                }
            }

            for (int r = 0; r < emptyCount; r++)
            {
                int newType = Random.Range(0, berrySprites.Length);
                grid[r, c].SetType(newType, berrySprites[newType]);
            }
        }
    }
}
