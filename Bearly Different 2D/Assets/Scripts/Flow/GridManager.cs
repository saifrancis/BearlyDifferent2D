using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class GridManager : MonoBehaviour
{
    public GameObject berryPrefab;
    public Transform gridPanel;
    public Sprite[] berrySprites;

    public int rows = 8;
    public int cols = 8;
    public Berry[,] grid;

    void Start() => GenerateGrid();

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

            if (col >= 2 && grid[row, col - 1].type == type && grid[row, col - 2].type == type)
                hasMatch = true;

            if (row >= 2 && grid[row - 1, col].type == type && grid[row - 2, col].type == type)
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

    public List<List<Berry>> FindMatchGroups()
    {
        var groups = new List<List<Berry>>();

        for (int r = 0; r < rows; r++)
        {
            int runType = -1, runStart = 0, runLen = 0;
            for (int c = 0; c <= cols; c++)
            {
                int t = (c < cols) ? grid[r, c].type : -999;
                if (t == runType && t != -1)
                    runLen++;
                else
                {
                    if (runType != -1 && runLen >= 3)
                    {
                        var g = new List<Berry>();
                        for (int cc = runStart; cc < runStart + runLen; cc++)
                            g.Add(grid[r, cc]);
                        groups.Add(g);
                    }
                    runType = t;
                    runStart = c;
                    runLen = (t != -1) ? 1 : 0;
                }
            }
        }

        for (int c = 0; c < cols; c++)
        {
            int runType = -1, runStart = 0, runLen = 0;
            for (int r = 0; r <= rows; r++)
            {
                int t = (r < rows) ? grid[r, c].type : -999;
                if (t == runType && t != -1)
                    runLen++;
                else
                {
                    if (runType != -1 && runLen >= 3)
                    {
                        var g = new List<Berry>();
                        for (int rr = runStart; rr < runStart + runLen; rr++)
                            g.Add(grid[rr, c]);
                        groups.Add(g);
                    }
                    runType = t;
                    runStart = r;
                    runLen = (t != -1) ? 1 : 0;
                }
            }
        }

        return groups;
    }

    public List<Berry> FlattenUnique(List<List<Berry>> groups)
    {
        var set = new HashSet<Berry>();
        foreach (var g in groups)
            foreach (var b in g)
                set.Add(b);
        return new List<Berry>(set);
    }

    public IEnumerator FlashMatches(List<Berry> matches, Color color, float duration)
    {
        List<Outline> outlines = new List<Outline>();

        foreach (Berry b in matches)
        {
            var outline = b.GetComponent<Outline>();
            if (outline != null)
            {
                outline.enabled = true;
                outline.effectColor = color;
                outline.effectDistance = new Vector2(6f, -5f); 
                outlines.Add(outline);
            }
        }

        yield return new WaitForSeconds(duration);

        foreach (var o in outlines)
            o.enabled = false;
    }

    public void RemoveAndCollapse(List<Berry> matches)
    {
        foreach (Berry b in matches)
            b.SetType(-1, null);

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
