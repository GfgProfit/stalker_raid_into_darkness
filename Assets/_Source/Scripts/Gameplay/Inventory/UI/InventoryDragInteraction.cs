using System;
using System.Collections.Generic;
using R3;
using RaidIntoDarkness.Inventory.Application;
using RaidIntoDarkness.Inventory.Domain;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RaidIntoDarkness.Inventory.UI
{
    internal sealed class InventoryDragInteraction : IDisposable
    {
        private const float DROP_AREA_FADE_DURATION = 0.15f;

        private readonly InventoryModel _model;
        private readonly List<InventoryGridViewBase> _grids;
        private readonly Transform _root;
        private readonly RectTransform _layer;
        private readonly CanvasGroup _dropArea;
        private readonly RectTransform _secondaryRoot;
        private readonly string _primaryContainerId;
        private readonly string _equipmentContainerId;
        private readonly Func<bool> _worldDropAvailable;
        private readonly InventoryDropAreaViewModel _dropAreaViewModel;
        private readonly IDisposable _dropAreaBinding;
        private readonly List<RaycastResult> _raycastResults = new();
        private readonly List<RectInt> _compatibleAreas = new();

        private InventoryDragState _drag;
        private IDisposable _modelBinding;
        private PointerEventData _raycastPointer;
        private EventSystem _eventSystem;
        private int? _pointerId;
        private float _dropAreaTargetAlpha;
        private bool _disposed;

        public InventoryDragInteraction(InventoryModel model, List<InventoryGridViewBase> grids, Transform root, RectTransform layer, CanvasGroup dropArea, RectTransform secondaryRoot, string primaryContainerId, string equipmentContainerId, Func<bool> worldDropAvailable, Func<ItemInstance, InventoryResult> drop)
        {
            _model = model;
            _grids = grids;
            _root = root;
            _layer = layer != null ? layer : root as RectTransform;
            _dropArea = dropArea;
            _secondaryRoot = secondaryRoot;
            _primaryContainerId = primaryContainerId;
            _equipmentContainerId = equipmentContainerId;
            _worldDropAvailable = worldDropAvailable;
            if (_dropArea != null)
            {
                _dropArea.interactable = false;
                _dropArea.blocksRaycasts = false;
                _dropArea.alpha = 0f;
                _dropAreaViewModel = new InventoryDropAreaViewModel(drop);
                _dropAreaBinding = _dropAreaViewModel.Visibility.Subscribe(visible => _dropAreaTargetAlpha = visible ? 1f : 0f);
            }
        }

        public InventoryDragState State => _drag;
        public InventoryWeaponAttachments Attachments { get; set; }
        public IEquipmentAnimationRequests Equipment { get; set; }

        public event Action Starting;

        public void BeginPointer(InventoryItemView view, PointerEventData pointer, InventoryTheme theme, IItemIconProvider icons)
        {
            if (pointer.button != PointerEventData.InputButton.Left || view == null || view.isActiveAndEnabled == false || view.Item == null)
            {
                return;
            }

            InventoryGridViewBase grid = view.Grid;
            if (grid != null && grid.GetItemView(view.Item) == view)
            {
                Begin(view.Item, grid, view.Item.Amount, pointer.position, pointer.pressEventCamera, theme, icons, pointer.pointerId);
            }
        }

        public bool Begin(ItemInstance item, InventoryGridViewBase sourceGrid, int amount, Vector2 screenPosition, Camera eventCamera, InventoryTheme theme, IItemIconProvider icons, int? pointerId)
        {
            if (_disposed || _layer == null || item == null || sourceGrid == null || _grids.Contains(sourceGrid) == false || IsCurrentSource(item, sourceGrid, null) == false)
            {
                return false;
            }

            Cancel();
            Starting?.Invoke();
            InventoryItemView sourceView = sourceGrid != null ? sourceGrid.GetItemView(item) : null;
            if (_disposed || IsCurrentSource(item, sourceGrid, sourceView) == false)
            {
                return false;
            }

            int resolvedAmount = Mathf.Clamp(amount, 1, item.Amount);
            bool rotated = sourceGrid.UsesSlotIcon(item, out _) == false && item.Location.Rotated;
            _drag = new InventoryDragState
            {
                Item = item,
                SourceGrid = sourceGrid,
                SourceView = sourceView,
                Rotated = rotated,
                Amount = resolvedAmount,
                IsSplit = resolvedAmount < item.Amount,
                Ghost = InventoryItemDragGhost.Create(_layer, item, sourceGrid.CellSize, rotated, theme, icons)
            };
            _pointerId = pointerId;
            _drag.Ghost.transform.SetAsLastSibling();

            if (_drag.IsSplit == false && sourceView != null)
            {
                sourceView.SetDragSource(true);
            }

            _modelBinding = Observable.FromEvent<InventoryChange>(handler => _model.Changed += handler, handler => _model.Changed -= handler).Subscribe(OnModelChanged);
            ShowCompatibleSlots(item);
            _dropAreaViewModel?.SetVisible(_drag.IsSplit == false && CanUseDropArea());
            Update(screenPosition, eventCamera);
            return true;
        }

        public bool MatchesPointer(InventoryItemView source, int pointerId) => _drag != null && _drag.SourceView == source && _pointerId == pointerId;

        public void ResetSource(InventoryItemView view)
        {
            if (_drag != null && _drag.SourceView == view)
            {
                Cancel();
            }
        }

        public bool TrackPointer(Vector2 screenPosition, Camera eventCamera)
        {
            if (_drag == null)
            {
                return false;
            }

            _drag.LastScreenPosition = screenPosition;
            _drag.EventCamera = eventCamera;
            return true;
        }

        public void Update(Vector2 screenPosition, Camera eventCamera)
        {
            if (ValidateSource() == false)
            {
                return;
            }

            TrackPointer(screenPosition, eventCamera);
            RectTransform ghostRect = _drag.Ghost.RectTransform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_layer, screenPosition, eventCamera, out Vector2 local);
            ghostRect.localPosition = local;
            RefreshDragTarget();
        }

        public void Rotate()
        {
            if (ValidateSource() == false || _drag.Item.Definition.AllowRotation == false)
            {
                return;
            }

            _drag.Rotated = _drag.Rotated == false;
            _drag.Ghost.SetRotation(_drag.Rotated, _drag.SourceGrid.CellSize);
            Update(_drag.LastScreenPosition, _drag.EventCamera);
        }

        public (InventoryResult Result, ItemInstance AttachmentSource, bool ReportFailure, InventorySound? Sound) Complete()
        {
            if (ValidateSource() == false)
            {
                return (InventoryResult.Fail(InventoryFailure.InvalidItem), null, false, null);
            }

            RefreshDragTarget();
            InventoryDragState drag = _drag;
            bool dropInWorld = IsOverDropArea(drag.LastScreenPosition, drag.EventCamera);
            string sourceContainerId = drag.Item.ContainerId;
            EndDrag();

            if (dropInWorld)
            {
                return (_dropAreaViewModel.Drop(drag.Item), null, false, null);
            }

            if (drag.TargetGrid == null || drag.TargetGrid.Container == null || drag.Kind == InventoryDropKind.None)
            {
                return (InventoryResult.Fail(InventoryFailure.NoSlot), null, false, null);
            }

            InventoryContainer target = drag.TargetGrid.Container;
            var applied = InventoryDragOperations.Apply(_model, Attachments, Equipment, drag.Item, target, drag.TargetItem, drag.TargetOrigin, drag.Rotated, drag.IsSplit, drag.Amount, drag.AutoPlace, drag.Kind != InventoryDropKind.Invalid, _equipmentContainerId, _primaryContainerId);
            if (applied.Attachment || applied.Queued)
            {
                return (applied.Result, applied.Attachment ? drag.Item : null, false, null);
            }

            InventorySound? sound = null;
            if (applied.Result.Success)
            {
                sound = target.Id == _equipmentContainerId ? InventorySound.ToSlot : sourceContainerId == _equipmentContainerId ? InventorySound.ToBag : null;
            }

            return (applied.Result, null, applied.Result.Success == false, sound);
        }

        public void Cancel()
        {
            if (_drag != null)
            {
                EndDrag();
            }
        }

        public void Suspend()
        {
            Cancel();
            _dropAreaViewModel?.SetVisible(false);
            _dropAreaTargetAlpha = 0f;
            if (_dropArea != null)
            {
                _dropArea.alpha = 0f;
            }
        }

        public void Tick()
        {
            if (_dropArea != null && _dropArea.alpha != _dropAreaTargetAlpha)
            {
                _dropArea.alpha = Mathf.MoveTowards(_dropArea.alpha, _dropAreaTargetAlpha, Time.unscaledDeltaTime / DROP_AREA_FADE_DURATION);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Suspend();
            _dropAreaBinding?.Dispose();
            _dropAreaViewModel?.Dispose();
            Starting = null;
            _raycastResults.Clear();
        }

        private bool IsCurrentSource(ItemInstance item, InventoryGridViewBase grid, InventoryItemView view)
        {
            if (grid == null || grid.Container == null || _model.ContainsItem(item) == false || _model.GetContainer(item.ContainerId) != grid.Container || grid.Container.Contains(item) == false)
            {
                return false;
            }

            return ReferenceEquals(view, null) || view != null && view.isActiveAndEnabled && view.Item == item && grid.GetItemView(item) == view;
        }

        private bool ValidateSource()
        {
            if (_drag == null)
            {
                return false;
            }

            if (IsCurrentSource(_drag.Item, _drag.SourceGrid, _drag.SourceView) && _drag.Ghost != null)
            {
                return true;
            }

            EndDrag();
            return false;
        }

        private void OnModelChanged(InventoryChange change)
        {
            if (ValidateSource() == false)
            {
                return;
            }

            InventoryGridViewBase target = _drag.TargetGrid;
            if (change.Item == _drag.Item || change.Affects(_equipmentContainerId))
            {
                ShowCompatibleSlots(_drag.Item);
            }

            if (change.Item == _drag.Item || change.Affects(_drag.SourceGrid.ContainerId) || (target != null && change.Affects(target.ContainerId)))
            {
                RefreshDragTarget();
            }
        }

        private void EndDrag()
        {
            InventoryDragState drag = _drag;
            _drag = null;
            _pointerId = null;
            _modelBinding?.Dispose();
            _modelBinding = null;
            _dropAreaViewModel?.SetVisible(false);

            if (drag.Ghost != null)
            {
                drag.Ghost.gameObject.SetActive(false);
                InventoryUIFactory.Destroy(drag.Ghost.gameObject);
            }

            if (drag.SourceView != null && drag.SourceView.Item == drag.Item)
            {
                drag.SourceView.SetDragSource(false);
            }

            foreach (InventoryGridViewBase grid in _grids)
            {
                if (grid != null)
                {
                    grid.ClearHighlights();
                }
            }
        }

        private bool CanUseDropArea() => _dropArea != null && _dropArea.isActiveAndEnabled && _worldDropAvailable() && (_secondaryRoot == null || _secondaryRoot.gameObject.activeSelf == false);

        private bool IsOverDropArea(Vector2 screenPosition, Camera eventCamera) => _dropAreaViewModel?.IsVisible == true && CanUseDropArea() && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_dropArea.transform, screenPosition, eventCamera);

        private void RefreshDragTarget()
        {
            Vector2 screenPosition = _drag.LastScreenPosition;
            Camera eventCamera = _drag.EventCamera;
            InventoryGridViewBase previousTarget = _drag.TargetGrid;
            InventoryGridViewBase target = IsOverDropArea(screenPosition, eventCamera) ? null : FindGridAt(screenPosition);

            if (previousTarget != null && previousTarget != target)
            {
                previousTarget.SetPlacementHighlight(default, Color.clear);
            }

            _drag.TargetGrid = target;
            _drag.TargetOrigin = default;
            _drag.Kind = InventoryDropKind.None;
            _drag.Failure = InventoryFailure.None;
            _drag.AutoPlace = false;
            _drag.TargetItem = null;

            if (target == null)
            {
                return;
            }

            Evaluate(target, screenPosition, eventCamera, out Vector2Int origin, out RectInt area, out InventoryDropKind kind, out InventoryFailure failure);
            _drag.TargetOrigin = origin;
            _drag.Kind = kind;
            _drag.Failure = failure;
            Color highlight = kind switch
            {
                InventoryDropKind.Valid => target.Style.ValidPlacementColor,
                InventoryDropKind.Merge => target.Style.MergePlacementColor,
                _ => target.Style.InvalidPlacementColor
            };

            target.SetPlacementHighlight(area, highlight);
        }

        private void ShowCompatibleSlots(ItemInstance item)
        {
            foreach (InventoryGridViewBase grid in _grids)
            {
                if (grid is not InventorySlotGridView slotGrid || slotGrid.Container is not SlottedContainer container)
                {
                    continue;
                }

                _compatibleAreas.Clear();

                foreach (SlotDefinition slot in container.Slots)
                {
                    ItemInstance occupant = container.GetSlotOccupant(slot.Id);

                    if (slot.Rule.AcceptsDefinition(item.Definition) && container.IsSlotAvailable(slot, item) && (slot.Mode == SlotMode.Grid || occupant == null || occupant == item))
                    {
                        _compatibleAreas.Add(slot.Bounds);
                    }
                }

                grid.SetCompatibleAreas(_compatibleAreas);
            }
        }

        private InventoryGridViewBase FindGridAt(Vector2 screenPosition)
        {
            EventSystem eventSystem = EventSystem.current;

            if (eventSystem == null)
            {
                return null;
            }

            if (_raycastPointer == null || _eventSystem != eventSystem)
            {
                _eventSystem = eventSystem;
                _raycastPointer = new PointerEventData(eventSystem);
            }

            _raycastPointer.Reset();
            _raycastPointer.position = screenPosition;
            _raycastResults.Clear();
            eventSystem.RaycastAll(_raycastPointer, _raycastResults);

            foreach (RaycastResult result in _raycastResults)
            {
                if (result.gameObject == null)
                {
                    continue;
                }

                InventoryGridViewBase grid = result.gameObject.GetComponentInParent<InventoryGridViewBase>();

                if (grid != null && _grids.Contains(grid))
                {
                    return grid.Container != null && _model.GetContainer(grid.ContainerId) == grid.Container ? grid : null;
                }

                if (result.gameObject.transform.IsChildOf(_root))
                {
                    return null;
                }
            }

            return null;
        }

        private void Evaluate(InventoryGridViewBase target, Vector2 screenPosition, Camera eventCamera, out Vector2Int origin, out RectInt area, out InventoryDropKind kind, out InventoryFailure failure)
        {
            InventoryContainer container = target.Container;
            ItemInstance item = _drag.Item;
            bool slotTarget = container is SlottedContainer;
            origin = default;
            area = default;
            failure = InventoryFailure.None;

            if (target.TryGetCellAtScreen(screenPosition, eventCamera, out Vector2Int targetCell))
            {
                ItemInstance targetItem = container.GetItemAt(targetCell);
                if (InventoryDragOperations.TryPreviewItemTarget(_model, Attachments, item, targetItem, _drag.IsSplit, _drag.Amount, out kind, out failure))
                {
                    _drag.TargetItem = targetItem;
                    origin = targetItem.Location.Position;
                    area = container.GetItemArea(targetItem);
                    return;
                }
            }

            if (target == _drag.SourceGrid && slotTarget == false)
            {
                kind = InventoryDropKind.None;
                return;
            }

            Vector2 pointer = target.ScreenToTopLeftLocal(screenPosition, eventCamera);
            Vector2Int size = item.GetSize(_drag.Rotated);
            origin = target.GetPlacementOrigin(pointer, size);
            area = new RectInt(origin, size);

            if (container.TryResolvePlacement(item, origin, _drag.Rotated, out ItemPlacement placement, out failure))
            {
                area = placement.Area;
            }

            var preview = InventoryDragOperations.PreviewPlacement(_model, item, container, origin, _drag.Rotated, _drag.IsSplit, _drag.Amount, _primaryContainerId, area);
            area = preview.Area;
            kind = preview.Kind;
            failure = preview.Failure;
            _drag.AutoPlace = preview.AutoPlace;
            if (kind == InventoryDropKind.Invalid)
            {
                ClipToContainer(ref area, container);
            }
        }

        private static void ClipToContainer(ref RectInt area, InventoryContainer container)
        {
            int xMin = Mathf.Clamp(area.xMin, 0, container.Width);
            int yMin = Mathf.Clamp(area.yMin, 0, container.Height);
            int xMax = Mathf.Clamp(area.xMax, 0, container.Width);
            int yMax = Mathf.Clamp(area.yMax, 0, container.Height);
            area = new RectInt(xMin, yMin, Mathf.Max(0, xMax - xMin), Mathf.Max(0, yMax - yMin));
        }
    }
}
