using DG.Tweening;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HorizontalSelector : MonoBehaviour
{
    [SerializeField] private CustomArrowButton _leftArrowButton;
    [SerializeField] private CustomArrowButton _rightArrowButton;
    [SerializeField] private TMP_Text _currentContentText;
    [SerializeField] private HorizontalLayoutGroup _horizontalLayoutGroup;
    [SerializeField] private RectTransform _itemPrefab;

    [Space]
    [SerializeField] private float _space = 10.0f;
    [SerializeField] private float _animationDuration = 0.2f;
    [SerializeField] private List<string> _contents;

    private readonly List<Image> _viewItems = new();
    private bool _isSetup;
    private bool _isSubscribed;

    public int Index { get; set; }
    public int OptionCount => _contents?.Count ?? 0;

    public event Action<int> IndexChanged;

    private void Start()
    {
        SubscribeArrows();

        if (_isSetup == false)
        {
            Setup(_contents);
        }
    }

    private void OnDestroy()
    {
        UnsubscribeArrows();
        _currentContentText?.rectTransform.DOKill();
    }

    public void Setup(List<string> contents)
    {
        _contents = contents ?? new List<string>();
        _isSetup = true;
        ClampIndex();

        ResetTextPosition();
        Apply();
        CreateViewItemLines();
        SelectViewItem();
        _leftArrowButton?.SetAvailable(OptionCount > 1);
        _rightArrowButton?.SetAvailable(OptionCount > 1);
    }

    public void SetOptions(IReadOnlyList<string> contents, int index)
    {
        bool sameOptions = _isSetup && _contents.Count == contents.Count;
        for (int i = 0; sameOptions && i < contents.Count; i++)
        {
            sameOptions = _contents[i] == contents[i];
        }
        if (sameOptions)
        {
            SetIndex(index);
            return;
        }
        Index = index;
        Setup(new List<string>(contents));
    }

    public void SetIndex(int index, bool notify = false)
    {
        int previous = Index;
        Index = index;
        ClampIndex();
        if (previous != Index)
        {
            ResetTextPosition();
            Apply();
            SelectViewItem();
        }

        if (notify)
        {
            NotifyIndexChanged();
        }
    }

    public string GetContentByIndex()
    {
        if (_contents == null || _contents.Count == 0)
        {
            return string.Empty;
        }

        return _contents[Index];
    }

    private void IncrementIndex()
    {
        if (OptionCount <= 1)
        {
            return;
        }

        Index++;

        if (Index >= _contents.Count)
        {
            Index = 0;
        }

        SelectViewItem();
        AnimateRightContentItem();
        NotifyIndexChanged();
    }

    private void DecrementIndex()
    {
        if (OptionCount <= 1)
        {
            return;
        }

        Index--;

        if (Index < 0)
        {
            Index = _contents.Count - 1;
        }

        SelectViewItem();
        AnimateLeftContentItem();
        NotifyIndexChanged();
    }

    private void Apply()
    {
        if (_currentContentText == null)
        {
            return;
        }

        _currentContentText.text = GetContentByIndex();
    }

    private void CreateViewItemLines()
    {
        ClearViewItemLines();

        if (_horizontalLayoutGroup == null || _itemPrefab == null || _contents == null || _contents.Count == 0)
        {
            return;
        }

        RectTransform rectTransform = _horizontalLayoutGroup.GetComponent<RectTransform>();
        float containerWidth = rectTransform.rect.width;

        float padding = _horizontalLayoutGroup.padding.left + _horizontalLayoutGroup.padding.right;
        float width = Mathf.Max(0.0f, containerWidth - padding);
        float spacing = Mathf.Min(_space, width / (2.0f * _contents.Count));
        _horizontalLayoutGroup.spacing = spacing;
        float itemWidth = (width - spacing * (_contents.Count - 1)) / _contents.Count;

        for (int i = 0; i < _contents.Count; i++)
        {
            RectTransform item = Instantiate(_itemPrefab, _horizontalLayoutGroup.transform);

            Vector2 size = item.sizeDelta;
            size.x = itemWidth;
            item.sizeDelta = size;

            if (item.TryGetComponent(out LayoutElement layoutElement))
            {
                layoutElement.minWidth = itemWidth;
                layoutElement.preferredWidth = itemWidth;
            }

            _viewItems.Add(item.GetComponent<Image>());
        }
    }

    private void SelectViewItem()
    {
        for (int i = 0; i < _viewItems.Count; i++)
        {
            if (_viewItems[i] == null)
            {
                continue;
            }

            if (i == Index)
            {
                Image viewItem = _viewItems[i];

                viewItem.DOColor(Color.white, _animationDuration).SetUpdate(true);
                viewItem.rectTransform.DOScaleY(1.3f, _animationDuration).SetUpdate(true);
            }
            else
            {
                _viewItems[i].DOColor(Color.gray, _animationDuration).SetUpdate(true);
                _viewItems[i].rectTransform.DOScaleY(1.0f, _animationDuration).SetUpdate(true);
            }
        }
    }

    private void ResetTextPosition()
    {
        if (_currentContentText != null)
        {
            RectTransform rect = _currentContentText.rectTransform;
            rect.DOKill();
            rect.localPosition = new Vector3(0.0f, rect.localPosition.y, rect.localPosition.z);
        }
    }

    private void AnimateLeftContentItem()
    {
        if (_currentContentText == null)
        {
            return;
        }

        RectTransform contentTextTransform = _currentContentText.rectTransform;
        contentTextTransform.DOKill();

        contentTextTransform.DOLocalMoveX(-350, _animationDuration).SetUpdate(true)
            .OnComplete(() =>
            {
                contentTextTransform.localPosition = new(350, 0, 0);
                contentTextTransform.DOLocalMoveX(0, _animationDuration).SetUpdate(true);
                Apply();
            }).SetEase(Ease.OutBack);
    }

    private void AnimateRightContentItem()
    {
        if (_currentContentText == null)
        {
            return;
        }

        RectTransform contentTextTransform = _currentContentText.rectTransform;
        contentTextTransform.DOKill();

        contentTextTransform.DOLocalMoveX(350, _animationDuration).SetUpdate(true)
            .OnComplete(() =>
            {
                contentTextTransform.localPosition = new(-350, 0, 0);
                contentTextTransform.DOLocalMoveX(0, _animationDuration).SetUpdate(true);
                Apply();
            }).SetEase(Ease.OutBack);
    }

    private void SubscribeArrows()
    {
        if (_isSubscribed)
        {
            return;
        }

        if (_leftArrowButton != null)
        {
            _leftArrowButton.OnClick.AddListener(DecrementIndex);
        }

        if (_rightArrowButton != null)
        {
            _rightArrowButton.OnClick.AddListener(IncrementIndex);
        }

        _isSubscribed = true;
    }

    private void UnsubscribeArrows()
    {
        if (_isSubscribed == false)
        {
            return;
        }

        if (_leftArrowButton != null)
        {
            _leftArrowButton.OnClick.RemoveListener(DecrementIndex);
        }

        if (_rightArrowButton != null)
        {
            _rightArrowButton.OnClick.RemoveListener(IncrementIndex);
        }

        _isSubscribed = false;
    }

    private void ClampIndex()
    {
        if (_contents == null || _contents.Count == 0)
        {
            Index = 0;
            return;
        }

        Index = Mathf.Clamp(Index, 0, _contents.Count - 1);
    }

    private void ClearViewItemLines()
    {
        for (int i = 0; i < _viewItems.Count; i++)
        {
            if (_viewItems[i] == null)
            {
                continue;
            }

            _viewItems[i].DOKill();
            _viewItems[i].rectTransform.DOKill();
            _viewItems[i].gameObject.SetActive(false);
            Destroy(_viewItems[i].gameObject);
        }

        _viewItems.Clear();
    }

    private void NotifyIndexChanged()
    {
        IndexChanged?.Invoke(Index);
    }
}
