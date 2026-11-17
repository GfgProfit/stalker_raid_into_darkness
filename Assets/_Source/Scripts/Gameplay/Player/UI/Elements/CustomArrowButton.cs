using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CustomArrowButton : Selectable, IPointerClickHandler, ISubmitHandler
{
    [SerializeField] private Graphic _target;
    [SerializeField] private float _animationDuration = 0.2f;

    [field: SerializeField] public UnityEvent OnClick { get; private set; } = new();

    protected override void Awake()
    {
        base.Awake();
        transition = Transition.None;
    }

    public void SetAvailable(bool value)
    {
        if (interactable == value)
        {
            return;
        }
        interactable = value;
        SetHighlight(false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            Activate();
        }
    }

    public void OnSubmit(BaseEventData eventData)
    {
        Activate();
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
        base.OnPointerEnter(eventData);
        SetHighlight(true);
    }

    public override void OnPointerExit(PointerEventData eventData)
    {
        base.OnPointerExit(eventData);
        SetHighlight(EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject);
    }

    public override void OnSelect(BaseEventData eventData)
    {
        base.OnSelect(eventData);
        SetHighlight(true);
    }

    public override void OnDeselect(BaseEventData eventData)
    {
        base.OnDeselect(eventData);
        SetHighlight(false);
    }

    protected override void OnDisable()
    {
        if (_target != null)
        {
            _target.DOKill();
            _target.rectTransform.DOKill();
        }
        base.OnDisable();
    }

    private void Activate()
    {
        if (!IsActive() || !IsInteractable())
        {
            return;
        }
        OnClick.Invoke();
        if (_target != null)
        {
            _target.DOKill();
            _target.DOColor(Color.gray, _animationDuration * 0.5f).SetUpdate(true).OnComplete(() => _target.DOColor(Color.white, _animationDuration * 0.5f).SetUpdate(true));
        }
    }

    private void SetHighlight(bool highlight)
    {
        if (_target == null)
        {
            return;
        }
        _target.DOKill();
        _target.rectTransform.DOKill();
        bool active = IsInteractable();
        Color color = active ? highlight ? Color.white : Color.gray : new Color(0.33f, 0.33f, 0.33f, 0.65f);
        _target.DOColor(color, _animationDuration).SetUpdate(true);
        _target.rectTransform.DOScale(highlight && active ? 1.1f : 1.0f, _animationDuration).SetUpdate(true);
    }
}
