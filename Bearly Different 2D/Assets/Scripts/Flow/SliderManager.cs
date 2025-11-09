using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SliderManager : MonoBehaviour
{
    [SerializeField] private Transform gameTransform;
    [SerializeField] private Transform piecePrefab;

    private List<Transform> pieces;
    private int emptyLocation;
    private int size;

    private int selectedIndex = 0;
    private Color originalColor = Color.white;
    private Color highlightColor = Color.red;

    [SerializeField] private TextMeshProUGUI messageText;

    [SerializeField] private GameObject helpPanel;          
    [SerializeField] private bool helpStartsVisible = true; 

    private readonly int[] targetLayout = new int[] { 3, 0, 2, 6, 1, 4, 8, 7, 5 };


    public UnityEngine.UI.Image BGSprite;
    public Sprite ColourSprite;

    [SerializeField] private ParticleSystem solveVFXPrefab; 
    [SerializeField] private float vfxScaleMultiplier = 1.1f; 
    [SerializeField] private string vfxSortingLayer = "Effects";
    [SerializeField] private int vfxSortingOrder = 50;

    public WinText wt;

    [SerializeField] private Vector3 solvedTargetPosition = new Vector3(0f, -2.5f, 0f); 
    [SerializeField] private float solvedTargetScale = 0.5f;                              
    [SerializeField] private float solveMoveDuration = 0.8f;                            
    [SerializeField]
    private AnimationCurve solveEase =
    AnimationCurve.EaseInOut(0f, 0f, 1f, 1f); 

    bool startShow = true;
    bool allowMove = false;

    void Start()
    {
        pieces = new List<Transform>();
        size = 3;
        CreateGamePieces(0.01f);

        if (helpPanel != null)
            helpPanel.SetActive(helpStartsVisible);

    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.H))
        {
            ToggleHelpPanel();
        }

        if (helpPanel.activeInHierarchy) return;

        if (startShow)
        {
            StartCoroutine(ShowSolvedThenApplyTarget());
            startShow = false;
        }

        if (!allowMove) return;

        HandleArrowKeys();
        HandleSpacebar();

        if (Input.GetKeyDown(KeyCode.R))
        {
            ApplyTargetLayout();
            if (messageText) messageText.text = "";
        }
    }

    private void ToggleHelpPanel()
    {
        if (helpPanel == null) return;
        helpPanel.SetActive(!helpPanel.activeSelf);
    }

    private void CreateGamePieces(float gapThickness)
    {
        float width = 1 / (float)size;

        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                Transform piece = Instantiate(piecePrefab, gameTransform);
                pieces.Add(piece);

                int index = (row * size) + col;
                piece.localPosition = IndexToLocalPosition(index);
                piece.localScale = ((2 * width) - gapThickness) * Vector3.one;
                piece.name = $"Piece {index}";

                if (piece.TryGetComponent<SpriteRenderer>(out SpriteRenderer sr))
                    sr.color = originalColor;

                if (row == size - 1 && col == size - 1)
                {
                    emptyLocation = index;
                    piece.gameObject.SetActive(false);
                }
                else
                {
                    float gap = gapThickness / 2f;
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

    private Vector3 IndexToLocalPosition(int index)
    {
        float width = 1f / size;
        int row = index / size;
        int col = index % size;

        return new Vector3(
            -1 + (2 * width * col) + width,
            +1 - (2 * width * row) - width,
            0
        );
    }

    private IEnumerator ShowSolvedThenApplyTarget()
    {
        for (int i = 0; i < pieces.Count; i++)
            pieces[i].localPosition = IndexToLocalPosition(i);

        emptyLocation = (size * size) - 1; 
        selectedIndex = 0;
        HighlightSelectedPiece();

        yield return new WaitForSeconds(3f);

        ApplyTargetLayout();
        allowMove = !helpPanel || !helpPanel.activeInHierarchy; ;
    }

    private void ApplyTargetLayout()
    {
        List<Transform> reordered = new List<Transform>(new Transform[size * size]);
        for (int slot = 0; slot < targetLayout.Length; slot++)
        {
            int tileIndex = targetLayout[slot]; 
            reordered[slot] = pieces[tileIndex];
        }

        pieces = reordered;

        emptyLocation = -1;
        for (int i = 0; i < pieces.Count; i++)
        {
            pieces[i].localPosition = IndexToLocalPosition(i);
            if (!pieces[i].gameObject.activeSelf) 
                emptyLocation = i;
        }

        if (emptyLocation == -1)
        {
            for (int i = 0; i < targetLayout.Length; i++)
            {
                if (targetLayout[i] == 8) { emptyLocation = i; break; }
            }
        }

        selectedIndex = 0;
        HighlightSelectedPiece();
        Debug.Log("Applied fixed target layout.");
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
                if (messageText) messageText.text = "";
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
        selectedIndex = i2;          
        HighlightSelectedPiece();

        if (CheckCompletion())
            StartCoroutine(GoToNextScene());
    }


    private bool CheckCompletion()
    {
        for (int i = 0; i < pieces.Count; i++)
        {
            if (pieces[i].name != $"Piece {i}")
                return false;
        }
        return true;
    }

    private IEnumerator GoToNextScene()
    {
        BGSprite.sprite = ColourSprite;
        PlaySolveVFX();

        yield return StartCoroutine(AnimateBoardOnSolve());

        wt.PlayWin();

        yield return new WaitForSeconds(5f);
        SceneManager.LoadScene("6Page_Six");
    }

    private IEnumerator ShowMessage(string text, float duration)
    {
        if (messageText) messageText.text = text;
        yield return new WaitForSeconds(duration);
        if (messageText) messageText.text = "";
    }

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

    public void GloveSelect() 
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

    private Bounds CalculatePuzzleBounds()
    {
        var bounds = new Bounds(gameTransform.position, Vector3.zero);
        bool hasAny = false;

        for (int i = 0; i < pieces.Count; i++)
        {
            var go = pieces[i].gameObject;
            if (!go.activeInHierarchy) continue;
            var r = go.GetComponent<Renderer>();
            if (r == null) continue;

            if (!hasAny) { bounds = r.bounds; hasAny = true; }
            else bounds.Encapsulate(r.bounds);
        }

        if (!hasAny) bounds = new Bounds(gameTransform.position, Vector3.one * 2f); 
        return bounds;
    }

    private void PlaySolveVFX()
    {
        if (!solveVFXPrefab) return;

        var b = CalculatePuzzleBounds();
        var ps = Instantiate(solveVFXPrefab, b.center, Quaternion.identity);

        float radius = Mathf.Max(b.extents.x, b.extents.y) * vfxScaleMultiplier;
        var shape = ps.shape;
        shape.enabled = true;
        if (shape.shapeType == ParticleSystemShapeType.Circle ||
            shape.shapeType == ParticleSystemShapeType.Donut)
        {
            shape.radius = radius;
        }
        else
        {
            ps.transform.localScale = Vector3.one * (radius * 1.0f);
        }

        var r = ps.GetComponent<Renderer>();
        if (r)
        {
            r.sortingLayerName = vfxSortingLayer;
            r.sortingOrder = vfxSortingOrder;
        }

        var main = ps.main;
        main.stopAction = ParticleSystemStopAction.Destroy;

        ps.Play();
    }

    private IEnumerator AnimateBoardOnSolve()
    {
        if (gameTransform == null) yield break;

        Vector3 startPos = gameTransform.position;
        Vector3 startScale = gameTransform.localScale;

        Vector3 endPos = solvedTargetPosition;
        Vector3 endScale = Vector3.one * solvedTargetScale;

        float t = 0f;
        float dur = Mathf.Max(0.01f, solveMoveDuration);

        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float e = solveEase.Evaluate(k);

            gameTransform.position = Vector3.LerpUnclamped(startPos, endPos, e);
            gameTransform.localScale = Vector3.LerpUnclamped(startScale, endScale, e);

            yield return null;
        }

        gameTransform.position = endPos;
        gameTransform.localScale = endScale;
    }
}
