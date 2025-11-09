using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class RollingCredits : MonoBehaviour
{
    public RectTransform viewport;
    public RectTransform content;

    public float scrollSpeed = 60f;
    public float waitAtEndSeconds = 3f;
    public string homeSceneName = "0HomePage";
    public TMP_FontAsset font;
    public int roleFontSize = 48;
    public int nameFontSize = 34;

    public float blockBottomSpacing = 30f;

    public float nameLineSpacing = 8f;

    public List<RoleBlock> teamCredits = new List<RoleBlock>()
    {
        new RoleBlock("Game Designers", new [] { "Eden Neave", "Amy Janse van Vuuren", "Saiyuri Francis" }),
        new RoleBlock("Engineer", new [] { "Eden Neave" }),
        new RoleBlock("Narrative/ Level Designer", new [] { "Am Janse van Vuuren" }),
        new RoleBlock("Animator/ Artist", new [] { "Saiyuri Francis" }),

        new RoleBlock("Voice Actors", new [] { "Angelica Johnston", "Zoe Phike", "Andiswa Gasa", "Katelyn Forbay" }),
    };

    public string specialThanksTitle = "Special Thanks To";
    public List<string> specialThanksNames = new List<string>()
    {
        "Ann Harding Cheshire Home", "Abdul-Khaaliq Mohamed", 
    };

    public string followUsTitle = "Follow Us";
    public List<string> followUsLines = new List<string>()
    {
        "Instagram: @bearlydifferent_",
    };

    private bool _startedScrolling;
    private float _targetScrollY; 
    private VerticalLayoutGroup _layout;
    private ContentSizeFitter _fitter;

    public Color titleColor = Color.black;
    public Color nameColor = Color.gray;

    [System.Serializable]
    public class RoleBlock
    {
        public string role;
        public List<string> names = new List<string>();

        public RoleBlock(string role, IEnumerable<string> names)
        {
            this.role = role;
            this.names = new List<string>(names);
        }
    }

    void Awake()
    {
        if (!viewport || !content)
        {
            Debug.LogError("RollingCredits: Please assign 'viewport' and 'content' RectTransforms.");
            enabled = false;
            return;
        }

        _layout = content.GetComponent<VerticalLayoutGroup>();
        if (_layout == null)
            _layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        _layout.childAlignment = TextAnchor.MiddleCenter;
        _layout.childControlHeight = true;
        _layout.childForceExpandHeight = false;
        _layout.childControlWidth = true;
        _layout.childForceExpandWidth = true;
        _layout.spacing = 0f;

        _fitter = content.GetComponent<ContentSizeFitter>();
        if (_fitter == null)
            _fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        _fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        var mask = viewport.GetComponent<Mask>();
        var img = viewport.GetComponent<Image>();
        if (img == null) img = viewport.gameObject.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0); 
        if (mask == null) mask = viewport.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;
    }

    void Start()
    {
        BuildCredits();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);

        Vector2 start = content.anchoredPosition;
        start.y = -viewport.rect.height * 0.5f - content.rect.height * 0.5f; 
        content.anchoredPosition = start;

        _targetScrollY = viewport.rect.height * 0.5f + content.rect.height * 0.5f;

        _startedScrolling = true;
    }

    void Update()
    {
        if (!_startedScrolling) return;

        var pos = content.anchoredPosition;
        pos.y = Mathf.MoveTowards(pos.y, _targetScrollY, scrollSpeed * Time.deltaTime);
        content.anchoredPosition = pos;

        if (Mathf.Approximately(pos.y, _targetScrollY))
        {
            _startedScrolling = false;
            StartCoroutine(FinishAndReturnHome());
        }
    }

    private System.Collections.IEnumerator FinishAndReturnHome()
    {
        yield return new WaitForSeconds(waitAtEndSeconds);
        if (!string.IsNullOrEmpty(homeSceneName))
            SceneManager.LoadScene(homeSceneName);
        else
            Debug.LogWarning("RollingCredits: homeSceneName not set.");
    }

    private void BuildCredits()
    {
        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);

        AddSpacer(80f);

        foreach (var block in teamCredits)
        {
            AddRoleBlock(block.role, block.names);
        }

        AddSpacer(40f);
        AddTitle(specialThanksTitle);
        AddNames(specialThanksNames);

        AddSpacer(40f);
        AddTitle(followUsTitle);
        AddNames(followUsLines);

        AddSpacer(80f);
    }

    private void AddRoleBlock(string role, List<string> names)
    {
        AddTitle(role);
        AddNames(names);
        AddSpacer(blockBottomSpacing);
    }

    private void AddTitle(string text)
    {
        var go = new GameObject("Title", typeof(RectTransform));
        go.transform.SetParent(content, false);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (font) tmp.font = font;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true;
        tmp.fontSize = roleFontSize;
        tmp.text = $"<b>{text}</b>";

        var layout = go.AddComponent<LayoutElement>();
        layout.minHeight = roleFontSize + 8;
        layout.preferredHeight = roleFontSize + 16;

        tmp.color = titleColor;

    }

    private void AddNames(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var go = new GameObject("Name", typeof(RectTransform));
            go.transform.SetParent(content, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (font) tmp.font = font;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            tmp.fontSize = nameFontSize;
            tmp.text = line;

            var layout = go.AddComponent<LayoutElement>();
            layout.minHeight = nameFontSize + nameLineSpacing;
            layout.preferredHeight = nameFontSize + nameLineSpacing + 6;

            tmp.color = nameColor;

        }
    }

    private void AddSpacer(float height)
    {
        var go = new GameObject("Spacer", typeof(RectTransform));
        go.transform.SetParent(content, false);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
    }
}
