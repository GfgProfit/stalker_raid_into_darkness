using System;
using System.Collections.Generic;
using UnityEngine;

namespace Inventory
{
    [CreateAssetMenu(menuName = "Inventory/Item Definition", fileName = "ItemDefinition")]
    public class ItemDefinition : ScriptableObject
    {
        [SerializeField] private string _definitionId;
        [SerializeField] private string _displayName;
        [SerializeField] private string _shortName;
        [SerializeField] [TextArea(3, 8)] private string _description;
        [SerializeField] private string _category = "Misc";
        [SerializeField] private List<string> _tags = new();
        [SerializeField] private Vector2Int _size = Vector2Int.one;
        [SerializeField] [Min(0f)] private float _weight;
        [SerializeField] [Min(0)] private int _maxStack = 1;
        [SerializeField] private bool _allowRotation = true;
        [SerializeField] private Color _backgroundColor = new(1f, 1f, 1f, 0.04f);
        [SerializeField] private Sprite _fallbackIcon;
        [SerializeField] private Sprite _iconOverride;
        [SerializeField] private GameObject _visualPrefab;
        [SerializeField] private List<ItemDefinition> _defaultIconVariant = new();
        [SerializeField] private ItemIconSettings _icon = new();
        [SerializeReference] private List<ItemDefinitionComponent> _components = new();

        public string DefinitionId => _definitionId ?? string.Empty;
        public string DisplayName => string.IsNullOrWhiteSpace(_displayName) ? name : _displayName;
        public string ShortName => _shortName ?? string.Empty;
        public string Description => _description ?? string.Empty;
        public string Category => _category ?? string.Empty;
        public IReadOnlyList<string> Tags => _tags;
        public Vector2Int Size => new(Mathf.Max(1, _size.x), Mathf.Max(1, _size.y));
        public int Width => Size.x;
        public int Height => Size.y;
        public float Weight => Mathf.Max(0f, _weight);
        public int MaxStack => _maxStack <= 0 ? int.MaxValue : _maxStack;
        public bool HasStackLimit => _maxStack > 0;
        public bool IsStackable => MaxStack > 1;
        public bool AllowRotation => _allowRotation && Width != Height;
        public Color BackgroundColor => _backgroundColor;
        public Sprite FallbackIcon => _iconOverride != null ? _iconOverride : _fallbackIcon;
        public Sprite IconOverride => _iconOverride;
        public GameObject VisualPrefab => _visualPrefab;
        public IReadOnlyList<ItemDefinition> DefaultIconVariant => _defaultIconVariant;
        public ItemIconSettings Icon => _icon;
        public IReadOnlyList<ItemDefinitionComponent> Components => _components;
        public bool CanGenerateIcon => _iconOverride == null && _icon != null && _icon.GenerateIcon && _visualPrefab != null;

        public bool HasTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || _tags == null)
            {
                return false;
            }

            for (int i = 0; i < _tags.Count; i++)
            {
                if (string.Equals(_tags[i], tag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsCategory(string category) => string.Equals(Category, category, StringComparison.OrdinalIgnoreCase);

        public T GetComponent<T>() where T : ItemDefinitionComponent
        {
            if (_components == null)
            {
                return null;
            }

            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] is T component)
                {
                    return component;
                }
            }

            return null;
        }

        public bool TryGetComponent<T>(out T component) where T : ItemDefinitionComponent
        {
            component = GetComponent<T>();
            return component != null;
        }

        public void SetDefinitionId(string definitionId) => _definitionId = definitionId;

        internal void Configure(string definitionId, string category, Vector2Int size, float weight, int maxStack, bool allowRotation, IEnumerable<string> tags = null)
        {
            _definitionId = definitionId;
            _category = category;
            _size = size;
            _weight = weight;
            _maxStack = maxStack;
            _allowRotation = allowRotation;
            _tags = tags == null ? new List<string>() : new List<string>(tags);
        }

        internal void SetVisual(GameObject visualPrefab, ItemIconSettings icon)
        {
            _visualPrefab = visualPrefab;
            _icon = icon ?? new ItemIconSettings();
        }

        internal void AddComponent(ItemDefinitionComponent component) => _components.Add(component);

        protected virtual void OnValidate()
        {
            _size = new Vector2Int(Mathf.Max(1, _size.x), Mathf.Max(1, _size.y));
            _maxStack = Mathf.Max(0, _maxStack);
            _weight = Mathf.Max(0f, _weight);
            _icon ??= new ItemIconSettings();
            _icon.Validate();
            _tags ??= new List<string>();
            _components ??= new List<ItemDefinitionComponent>();
        }
    }
}
