using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class SliderManager : MonoBehaviour
{
    [SerializeField] private Transform gameTransform;
    [SerializeField] private Transform piecePrefab;

    private List<Transform> pieces;
    private int emptyLocation;
    private int size;
    private bool shuffling = false;
    private bool hasShuffled = false;

    private int selectedIndex = 0;
    private Color originalColor = Color.white;
    private Color highlightColor = Color.red;

    [SerializeField] private TextMeshProUGUI messageText;

    private void CreateGamePieces(float gapThickness)
    {
        float width = 1 / (float)size;
        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                Transform piece = Instantiate(piecePrefab, gameTransform);
                pieces.Add(piece);

                piece.localPosition = new Vector3(-1 + (2 * width * col) + width,
                                                  +1 - (2 * width * row) - width, 0);

                piece.localScale = ((2 * width) - gapThickness) * Vector3.one;
                piece.name = $"Piece {(row * size) + col}";

                if (piece.TryGetComponent<SpriteRenderer>(out SpriteRenderer sr))
                {
                    sr.color = originalColor;
                }

                if ((row == size - 1) && (col == size - 1))
                {
                    emptyLocation = (size * size) - 1;
                    piece.gameObject.SetActive(false);
                }
                else
                {
                    float gap = gapThickness / 2;
                    Mesh mesh = piece.GetComponent<MeshFilter>().mesh;
                    Vector2[] uv = new Vector2[4];

                    uv[0] = new Vector2((width * col) + gap, 1 - ((width * (row + 1)) - gap));
                    uv[1] = new Vector2((width * (col + 1)) - gap, 1 - ((width * (row + 1)) - gap));
                    uv[2] = new Vector2((width * col) + gap, 1 - ((width * row) + gap));
                    uv[3] = new Vector2((width * (col + 1)) - gap, 1 - ((width * row) + gap));

                    mesh.uv = uv;
                }
            }
        }

        HighlightSelectedPiece();
    }

    void Start()
    {
        pieces = new List<Transform>();
        size = 3;
        CreateGamePieces(0.01f);
    }

    void Update()
    {
        if (!shuffling && !hasShuffled && CheckCompletion())
        {
            shuffling = true;
            StartCoroutine(WaitShuffle(3f));
        }

        if (hasShuffled && !shuffling && Input.GetKeyDown(KeyCode.R))
        {
            StartCoroutine(Reshuffle());
        }

        HandleArrowKeys();
        HandleSpacebar();
    }

    private void HandleArrowKeys()
    {
        int previousIndex = selectedIndex;

        if (Input.GetKeyDown(KeyCode.LeftArrow) && (selectedIndex % size > 0))
            selectedIndex--;
        if (Input.GetKeyDown(KeyCode.RightArrow) && (selectedIndex % size < size - 1))
            selectedIndex++;
        if (Input.GetKeyDown(KeyCode.UpArrow) && (selectedIndex >= size))
            selectedIndex -= size;
        if (Input.GetKeyDown(KeyCode.DownArrow) && (selectedIndex < pieces.Count - size))
            selectedIndex += size;

        if (previousIndex != selectedIndex)
            HighlightSelectedPiece();
    }

    private void HighlightSelectedPiece()
    {
        for (int i = 0; i < pieces.Count; i++)
        {
            Transform outline = pieces[i].Find("Outline"); 
            if (outline != null)
                outline.gameObject.SetActive(i == selectedIndex);
        }
    }


    private void HandleSpacebar()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
           
            if (IsAdjacent(selectedIndex, emptyLocation))
            {
                SwapPieces(selectedIndex, emptyLocation);
                messageText.text = "";
            }
            else
            {
                Debug.Log("Piece can't be moved");
                StartCoroutine(ShowMessage("Piece can't be moved", 2f));
            }
        }
    }

    private bool IsAdjacent(int index1, int index2)
    {
        int row1 = index1 / size;
        int col1 = index1 % size;
        int row2 = index2 / size;
        int col2 = index2 % size;

        return (Mathf.Abs(row1 - row2) + Mathf.Abs(col1 - col2)) == 1;
    }

    private void SwapPieces(int i1, int i2)
    {
        (pieces[i1], pieces[i2]) = (pieces[i2], pieces[i1]);
        (pieces[i1].localPosition, pieces[i2].localPosition) = (pieces[i2].localPosition, pieces[i1].localPosition);
        emptyLocation = i1;

        if (hasShuffled && CheckCompletion())
        {
            StartCoroutine(GoToNextScene());
        }
    }

    private bool CheckCompletion()
    {
        for (int i = 0; i < pieces.Count; i++)
        {
            if (pieces[i].name != $"Piece {i}")
            {
                return false;
            }
        }
        return true;
    }

    private IEnumerator WaitShuffle(float duration)
    {
        yield return new WaitForSeconds(duration);
        Shuffle();
        hasShuffled = true;
        shuffling = false;
    }

    private void Shuffle()
    {
        int count = 0;
        int last = 0;

        while (count < (size * size * size))
        {
            int rnd = Random.Range(0, size * size);

            if (rnd == last) { continue; }
            last = rnd;

            if (SwapIfValid(rnd, -size, size))
            {
                count++;
            }
            else if (SwapIfValid(rnd, +size, size))
            {
                count++;
            }
            else if (SwapIfValid(rnd, -1, 0))
            {
                count++;
            }
            else if (SwapIfValid(rnd, +1, size - 1))
            {
                count++;
            }
        }
    }

    private IEnumerator Reshuffle()
    {
        shuffling = true;
        yield return new WaitForSeconds(0.5f);
        Shuffle();
        shuffling = false;
    }

    private IEnumerator GoToNextScene()
    {
        yield return new WaitForSeconds(5f);
        SceneManager.LoadScene("6Page_Six");
    }

    private bool SwapIfValid(int i, int offset, int colCheck)
    {
        if ((i % size) != colCheck && ((i + offset) == emptyLocation))
        {
            SwapPieces(i, i + offset);
            return true;
        }
        return false;
    }

    private IEnumerator ShowMessage(string text, float duration)
    {
        messageText.text = text;
        yield return new WaitForSeconds(duration);
        messageText.text = "";
    }

    // ⬇️ Put these inside your existing SliderManager class

// --- Public API used by the glove ---
public void GloveMoveLeft()
{
    int previous = selectedIndex;
    if (selectedIndex % size > 0) selectedIndex--;
    if (previous != selectedIndex) HighlightSelectedPiece();
}

public void GloveMoveRight()
{
    int previous = selectedIndex;
    if (selectedIndex % size < size - 1) selectedIndex++;
    if (previous != selectedIndex) HighlightSelectedPiece();
}

public void GloveMoveUp()
{
    int previous = selectedIndex;
    if (selectedIndex >= size) selectedIndex -= size;
    if (previous != selectedIndex) HighlightSelectedPiece();
}

public void GloveMoveDown()
{
    int previous = selectedIndex;
    if (selectedIndex < pieces.Count - size) selectedIndex += size;
    if (previous != selectedIndex) HighlightSelectedPiece();
}

public void GloveSelect()  // same as pressing Space
{
    if (IsAdjacent(selectedIndex, emptyLocation))
    {
        SwapPieces(selectedIndex, emptyLocation);
        if (messageText != null) messageText.text = "";
    }
    else
    {
        if (messageText != null) StartCoroutine(ShowMessage("Piece can't be moved", 2f));
    }
}

}
