using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Source.Canvas;
using Source.MetaBlocks;
using Source.Model;
using Source.Ui.AssetsInventory.Models;
using Source.Ui.AssetsInventory.slots;
using Source.Ui.Menu;
using Source.Ui.TabPane;
using Source.Ui.Utils;
using Source.Utils;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Source.Ui.AssetsInventory
{
    public class AssetsInventory : MonoBehaviour
    {
        private static AssetsInventory instance;
        private static readonly string HANDY_SLOTS_KEY = "HANDY_SLOTS";

        private VisualElement root;
        private VisualElement inventory;
        private VisualElement handyPanel;
        private VisualElement handyPanelRoot;
        private Button openCloseInvButton;
        private VisualElement hammerModeArea;
        private ScrollView handyBar;

        private Sprite closeIcon;
        private Sprite openIcon;
        private Sprite addToFavoriteIcon;
        private Sprite removeFromFavoriteIcon;
        private Sprite hammerIcon;

        private readonly AssetsRestClient restClient = new();
        private readonly Dictionary<int, Pack> packs = new();
        private Category selectedCategory;
        private string filterText;

        private List<InventorySlotWrapper> handyBarSlots = new();

        private InventorySlot selectedSlot;
        public readonly UnityEvent<SlotInfo> selectedSlotChanged = new();

        [SerializeField] private ColorSlotPicker colorSlotPicker;
        private Foldout colorBlocksFoldout;
        private List<FavoriteItem> favoriteItems;
        private TabPane.TabPane tabPane;
        private VisualElement breadcrumb;
        private VisualElement inventoryContainer;
        private bool firstTime = true;
        private int selectedHandySlotIndex = -1;
        private UnityAction<bool> focusListener;
        private SimpleInventorySlot hammerSlot;

        void Start()
        {
            openIcon = Resources.Load<Sprite>("Icons/openPane");
            closeIcon = Resources.Load<Sprite>("Icons/closePane");

            addToFavoriteIcon = Resources.Load<Sprite>("Icons/whiteHeart");
            removeFromFavoriteIcon = Resources.Load<Sprite>("Icons/redHeart");

            hammerIcon = Resources.Load<Sprite>("Icons/hammer");

            GameManager.INSTANCE.stateChange.AddListener(_ => UpdateVisibility());
            Player.INSTANCE.viewModeChanged.AddListener(_ => UpdateVisibility());
            UpdateVisibility();

            Player.INSTANCE.InitOnSelectedAssetChanged(); // TODO ?
        }

        void OnEnable()
        {
            if (firstTime)
            {
                firstTime = false;
                instance = this;
                PlayerPrefs.SetString(HANDY_SLOTS_KEY, "[]");
            }

            root = GetComponent<UIDocument>().rootVisualElement;
            inventory = root.Q<VisualElement>("inventory");
            inventoryContainer = root.Q<VisualElement>("inventoryContainer");

            var tabConfigurations = new List<TabConfiguration>
            {
                new("Assets", "Ui/AssetInventory/AssetsTab", SetupAssetsTab),
                new("Blocks", "Ui/AssetInventory/BlocksTab", SetupBlocksTab),
                new("Favorites", "Ui/AssetInventory/FavoritesTab", SetupFavoritesTab)
            };
            tabPane = new TabPane.TabPane(tabConfigurations);
            var s = tabPane.style;
            s.paddingBottom = s.paddingTop = 6;
            s.flexGrow = 1;
            s.width = new StyleLength(new Length(100, LengthUnit.Percent));
            inventory.Add(tabPane);

            handyPanelRoot = root.Q<VisualElement>("handyPanelRoot");
            handyPanel = root.Q<VisualElement>("handyPanel");
            handyBar = handyPanel.Q<ScrollView>("handyBar");
            Utils.Utils.IncreaseScrollSpeed(handyBar, 600);
            openCloseInvButton = handyPanel.Q<Button>("openCloseInvButton");
            openCloseInvButton.clickable.clicked += ToggleInventory;

            hammerModeArea = handyPanel.Q<VisualElement>("hammerModeArea");
            hammerSlot = new SimpleInventorySlot();
            hammerSlot.HideSlotBackground();
            hammerSlot.SetBackground(hammerIcon);
            hammerSlot.SetSlotInfo(new SlotInfo());
            hammerSlot.SetSize(80, 10);
            hammerModeArea.Add(hammerSlot.VisualElement());

            handyBarSlots.Clear();
            handyBar.Clear();
            var savedHandySlots = GetSavedHandySlots();
            savedHandySlots.Reverse();
            foreach (var savedHandySlot in savedHandySlots)
                AddToHandyPanel(savedHandySlot);

            var locked = MouseLook.INSTANCE.cursorLocked;
            root.focusable = !locked;
            root.SetEnabled(!locked);
            if (focusListener != null)
                MouseLook.INSTANCE.cursorLockedStateChanged.RemoveListener(focusListener);
            focusListener = locked =>
            {
                root.focusable = !locked;
                root.SetEnabled(!locked);
                if (inventoryContainer.style.visibility == Visibility.Visible)
                    ToggleInventory();
            };
            MouseLook.INSTANCE.cursorLockedStateChanged.AddListener(focusListener);
        }

        private void UpdateVisibility()
        {
            var active = (GameManager.INSTANCE.GetState() == GameManager.State.PLAYING ||
                          GameManager.INSTANCE.GetState() == GameManager.State.MOVING_OBJECT)
                         && Player.INSTANCE.GetViewMode() == Player.ViewMode.FIRST_PERSON
                         && !Settings.IsGuest()
                ; // && Can Edit Land 
            gameObject.SetActive(active);
            inventoryContainer.style.visibility = Visibility.Visible; // is null at start and can't be checked !
            ToggleInventory();
            if (active)
                LoadFavoriteItems(() =>
                {
                    if (handyBarSlots.Count == 0)
                    {
                        foreach (var favoriteItem in favoriteItems)
                            AddToHandyPanel(favoriteItem.ToSlotInfo());
                    }

                    SelectSlot(null);
                }, () =>
                {
                    SelectSlot(null);
                    // FIXME: what to do on error?
                });
        }

        private void SetupAssetsTab()
        {
            selectedCategory = null;
            var searchCriteria = new SearchCriteria
            {
                limit = 100
            };
            var loadingId = LoadingLayer.LoadingLayer.Show(inventory);
            StartCoroutine(restClient.GetPacks(searchCriteria, packs =>
            {
                foreach (var pack in packs)
                    this.packs[pack.id] = pack;
                LoadingLayer.LoadingLayer.Hide(loadingId);
            }, () => LoadingLayer.LoadingLayer.Hide(loadingId)));

            var loadingId2 = LoadingLayer.LoadingLayer.Show(inventory);
            StartCoroutine(restClient.GetCategories(searchCriteria, categories =>
            {
                var scrollView = tabPane.GetTabBody().Q<ScrollView>("categories");
                scrollView.Clear();
                Utils.Utils.IncreaseScrollSpeed(scrollView, 600);
                scrollView.mode = ScrollViewMode.Vertical;
                scrollView.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
                foreach (var category in categories)
                    scrollView.Add(CreateCategoriesListItem(category));
                LoadingLayer.LoadingLayer.Hide(loadingId2);
            }, () => LoadingLayer.LoadingLayer.Hide(loadingId2)));

            var searchField = tabPane.GetTabBody().Q<TextField>("searchField");
            searchField.multiline = false;
            Utils.Utils.SetPlaceHolderForTextField(searchField, "Search");
            Utils.Utils.RegisterUiEngagementCallbacksForTextField(searchField);

            IEnumerator searchCoroutine = null;
            searchField.RegisterValueChangedCallback(evt =>
            {
                if (searchCoroutine != null)
                    StopCoroutine(searchCoroutine);
                searchCoroutine = DebounceSearchField(evt.newValue);
                StartCoroutine(searchCoroutine);
            });
        }

        private IEnumerator DebounceSearchField(string value)
        {
            yield return new WaitForSeconds(0.6f);
            filterText = value;
        }

        private void SetupBlocksTab()
        {
            var scrollView = tabPane.GetTabBody().Q<ScrollView>("blockPacks");
            scrollView.Clear();
            Utils.Utils.IncreaseScrollSpeed(scrollView, 600);
            scrollView.mode = ScrollViewMode.Vertical;
            scrollView.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;

            var regularBlocksFoldout = CreateBlocksPackFoldout("Regular Blocks",
                Blocks.GetBlockTypes().Where(blockType => blockType is not MetaBlockType && blockType.name != "air")
                    .ToList());

            var metaBlocksFoldout = CreateBlocksPackFoldout("Meta Blocks",
                Blocks.GetBlockTypes().Where(blockType => blockType is MetaBlockType).ToList());

            colorSlotPicker.SetOnColorCreated(color =>
            {
                ColorBlocks.SaveBlockColor(color);
                UpdateUserColorBlocks();
            });
            colorSlotPicker.transform.localScale = new Vector3(2.5f, 2.5f, 2.5f);

            scrollView.Add(regularBlocksFoldout);
            scrollView.Add(metaBlocksFoldout);

            colorBlocksFoldout = CreateColorBlocksFoldout();
            UpdateUserColorBlocks();
            scrollView.Add(colorBlocksFoldout);
        }

        private void SetupFavoritesTab()
        {
            LoadFavoriteItems(() =>
            {
                var scrollView = tabPane.GetTabBody().Q<ScrollView>("favorites");
                var container = new VisualElement();
                for (int i = 0; i < favoriteItems.Count; i++)
                {
                    var favoriteItem = favoriteItems[i];
                    var slot = new FavoriteItemInventorySlot(favoriteItem);
                    GridUtils.SetChildPosition(slot.VisualElement(), 80, 80, i, 3);
                    container.Add(slot.VisualElement());
                }

                GridUtils.SetContainerSize(container, favoriteItems.Count, 90, 3);
                scrollView.Add(container);
            }, () =>
            {
                //TODO: show error snack
            }, true);
        }

        private void Update()
        {
            if (filterText != null)
            {
                FilterAssets(filterText);
                filterText = null;
            }

            if (selectedSlot != null && (Input.GetButtonDown("Clear slot selection") || Input.GetMouseButtonDown(1)) &&
                !Player.INSTANCE.SelectionActiveBeforeAtFrameBeginning)
                SelectSlot(null);

            if (handyBar.childCount == 0 || !MouseLook.INSTANCE.cursorLocked)
                return;

            var mouseDelta = Input.mouseScrollDelta.y;
            var inc = Input.GetButtonDown("Change Block") || mouseDelta <= -0.1;
            var dec = Input.GetButtonDown("Change Block") &&
                      (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                      || mouseDelta >= 0.1;
            if (!dec && !inc)
                return;
            if (dec)
            {
                if (selectedHandySlotIndex is -1 or 0)
                    selectedHandySlotIndex = handyBar.childCount - 1;
                else
                    selectedHandySlotIndex--;
            }
            else
            {
                if (selectedHandySlotIndex == handyBar.childCount - 1)
                    selectedHandySlotIndex = 0;
                else
                    selectedHandySlotIndex++;
            }

            handyBar.ScrollTo(handyBarSlots[selectedHandySlotIndex].VisualElement());
            SelectSlot(handyBarSlots[selectedHandySlotIndex], false);
        }

        private void ToggleInventory()
        {
            var isVisible = inventoryContainer.style.visibility == Visibility.Visible;
            inventoryContainer.style.visibility = isVisible ? Visibility.Hidden : Visibility.Visible;
            handyPanel.style.right = isVisible ? 5 : 362;
            var background = new StyleBackground
            {
                value = Background.FromSprite(isVisible ? openIcon : closeIcon)
            };
            openCloseInvButton.style.backgroundImage = background;
            if (!isVisible)
                OpenAssetsTab();
        }

        private void LoadFavoriteItems(Action onDone = null, Action onFail = null, bool forFavoritesTab = false)
        {
            var loadingId = LoadingLayer.LoadingLayer.Show(forFavoritesTab ? inventory : handyPanelRoot);
            StartCoroutine(restClient.GetAllFavoriteItems(new SearchCriteria(), favItems =>
            {
                favoriteItems = favItems;
                onDone?.Invoke();
                LoadingLayer.LoadingLayer.Hide(loadingId);
            }, () =>
            {
                LoadingLayer.LoadingLayer.Hide(loadingId);
                onFail?.Invoke();
            }, this));
        }

        private void UpdateUserColorBlocks()
        {
            if (colorBlocksFoldout.childCount == 2)
                colorBlocksFoldout.RemoveAt(1);
            var colorSlotsContainer = CreateUserColorBlocks();
            colorBlocksFoldout.contentContainer.Add(colorSlotsContainer);
            colorBlocksFoldout.contentContainer.style.height = colorSlotsContainer.style.height.value.value + 75;
        }

        private Foldout CreateColorBlocksFoldout()
        {
            var foldout = CreatePackFoldout("Color Blocks");
            var colorBlockCreator = Utils.Utils.Create("Ui/AssetInventory/ColorBlockCreator");
            var colorPickerToggle = colorBlockCreator.Q<Button>();
            colorPickerToggle.style.height = 70;
            colorPickerToggle.clickable.clicked += () => colorSlotPicker.ToggleColorPicker();
            foldout.contentContainer.Add(colorPickerToggle);
            return foldout;
        }

        private VisualElement CreateUserColorBlocks()
        {
            var playerColorBlocks = ColorBlocks.GetPlayerColorBlocks();
            var size = playerColorBlocks.Count;
            var slotsContainer = new VisualElement();
            for (var i = 0; i < size; i++)
            {
                var slot = new ColorBlockInventorySlot();
                ColorUtility.TryParseHtmlString(playerColorBlocks[i], out var color);
                slot.SetSlotInfo(new SlotInfo(ColorBlocks.GetBlockTypeFromColor(color)));
                slot.SetSize(80);
                slot.SetGridPosition(i, 3);
                SetupFavoriteAction(slot);
                slotsContainer.Add(slot.VisualElement());
            }

            slotsContainer.style.height = 90 * (size / 3 + 1);
            return slotsContainer;
        }

        public void DeleteColorBlock(ColorBlockInventorySlot colorBlockInventorySlot)
        {
            ColorBlocks.RemoveBlockColorFromSaving(colorBlockInventorySlot.color);
            UpdateUserColorBlocks();
        }

        private Foldout CreateBlocksPackFoldout(string name, List<BlockType> blocks)
        {
            var foldout = CreatePackFoldout(name);

            var size = blocks.Count;
            if (size <= 0) return foldout;
            for (var i = 0; i < size; i++)
            {
                var slot = new BlockInventorySlot();
                var slotInfo = new SlotInfo(blocks[i]);
                slot.SetSlotInfo(slotInfo);
                slot.SetSize(80);
                slot.SetGridPosition(i, 3);
                SetupFavoriteAction(slot);
                foldout.contentContainer.Add(slot.VisualElement());
            }

            GridUtils.SetContainerSize(foldout.contentContainer, size, 90, 3);
            return foldout;
        }

        private static Foldout CreatePackFoldout(string name)
        {
            var foldout = new Foldout
            {
                text = name
            };
            foldout.SetValueWithoutNotify(true);
            var fs = foldout.style;
            fs.marginRight = fs.marginLeft = fs.marginBottom = fs.marginTop = 5;
            return foldout;
        }

        private void FilterAssets(string filter)
        {
            if (filter.Length == 0)
            {
                tabPane.OpenTab(0);
                return;
            }

            var sc = new SearchCriteria
            {
                limit = 100,
                searchTerms = new Dictionary<string, object>
                {
                    {"generalSearch", filter},
                }
            };
            if (selectedCategory != null)
                sc.searchTerms.Add("category", selectedCategory.id);
            var scrollView = CreateAssetsScrollView(sc, true);
            SetAssetsTabContent(scrollView, OpenAssetsTab, "Categories");
        }

        private void OpenAssetsTab()
        {
            tabPane.OpenTab(0);
            breadcrumb = tabPane.GetTabBody().Q("breadcrumb");
            breadcrumb.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.None);
        }

        private VisualElement CreateCategoriesListItem(Category category)
        {
            var container = new VisualElement();
            var categoryButton = Utils.Utils.Create("Ui/AssetInventory/CategoryButton");
            container.style.paddingTop = container.style.paddingBottom = 3;
            container.Add(categoryButton);

            var label = categoryButton.Q<Label>("label");
            label.text = category.name;

            var image = categoryButton.Q("image");

            StartCoroutine(UiImageUtils.SetBackGroundImageFromUrl(category.thumbnailUrl,
                Resources.Load<Sprite>("Icons/loading"), image));

            categoryButton.Q<Button>().clickable.clicked += () =>
            {
                selectedCategory = category;
                var searchCriteria = new SearchCriteria
                {
                    limit = 100,
                    searchTerms = new Dictionary<string, object> {{"category", category.id}}
                };
                var scrollView = CreateAssetsScrollView(searchCriteria);
                SetAssetsTabContent(scrollView, OpenAssetsTab, category.name);
            };
            return container;
        }

        private void SetAssetsTabContent(VisualElement visualElement, Action onBack, string backButtonText = "Back")
        {
            var assetsTabContent = tabPane.GetTabBody().Q<VisualElement>("content");
            var categoriesView = tabPane.GetTabBody().Q<ScrollView>("categories");
            assetsTabContent.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.Flex);
            categoriesView.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.None);
            SetBodyContent(assetsTabContent, visualElement, onBack, backButtonText);
        }


        private ScrollView CreateAssetsScrollView(SearchCriteria searchCriteria, bool isSearchResult = false)
        {
            var scrollView = new ScrollView(ScrollViewMode.Vertical)
            {
                scrollDecelerationRate = 0.135f,
                verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible,
                horizontalScrollerVisibility = ScrollerVisibility.Hidden
            };
            Utils.Utils.IncreaseScrollSpeed(scrollView, 600);
            var ss = scrollView.style;
            ss.height = new StyleLength(new Length(90, LengthUnit.Percent));
            ss.width = new StyleLength(new Length(90, LengthUnit.Percent));
            ss.flexGrow = 1;

            searchCriteria.limit = 20;

            foreach (var pack in packs)
            {
                var foldout = CreatePackFoldout(pack.Value.name);
                foldout.SetValueWithoutNotify(false);
                foldout.RegisterValueChangedCallback(evt =>
                {
                    foldout.contentContainer.Clear();
                    var label = new Label("No items found")
                    {
                        style =
                        {
                            unityTextAlign = TextAnchor.MiddleCenter,
                            width = new StyleLength(new Length(100, LengthUnit.Percent)),
                            display = new StyleEnum<DisplayStyle>(DisplayStyle.None)
                        }
                    };
                    foldout.contentContainer.Add(label);
                    foldout.contentContainer.Add(new VisualElement()); // Slots container

                    if (searchCriteria.searchTerms.ContainsKey("pack"))
                        searchCriteria.searchTerms.Remove("pack");
                    searchCriteria.searchTerms.Add("pack", pack.Key);
                    var loadMore = Utils.Utils.Create("Ui/AssetInventory/LoadMoreButton");
                    var loadMoreButton = loadMore.Q<Button>();
                    loadMoreButton.clickable.clicked += () => LoadAPageOfAssetsIntoFoldout(foldout, searchCriteria);
                    foldout.contentContainer.Add(loadMore);
                    if (evt.newValue)
                        LoadAPageOfAssetsIntoFoldout(foldout, searchCriteria);
                });
                if (isSearchResult)
                    foldout.schedule.Execute(() => foldout.value = true);

                scrollView.Add(foldout);
            }

            return scrollView;
        }

        private void LoadAPageOfAssetsIntoFoldout(Foldout foldout, SearchCriteria searchCriteria)
        {
            searchCriteria.limit = 15;
            var content = foldout.contentContainer.Children().ElementAt(1);
            if (content.childCount > 0)
            {
                var slot = content.ElementAt(content.childCount - 1).userData as AssetInventorySlot;
                searchCriteria.lastId = slot.GetSlotInfo().asset.id.Value;
            }
            else
                searchCriteria.lastId = null;

            var loadingId = LoadingLayer.LoadingLayer.Show(inventory);
            StartCoroutine(restClient.GetAssets(searchCriteria, assets =>
            {
                var empty = content.childCount == 0 && assets.Count == 0;
                foldout.contentContainer.Children().ElementAt(0).style.display =
                    new StyleEnum<DisplayStyle>(empty ? DisplayStyle.Flex : DisplayStyle.None);
                if (!empty)
                    AddAssetsToFoldout(foldout, assets);
                else
                    foldout.contentContainer.style.height = 75;
                LoadingLayer.LoadingLayer.Hide(loadingId);
            }, () => LoadingLayer.LoadingLayer.Hide(loadingId)));
        }

        private void AddAssetsToFoldout(Foldout foldout, List<Asset> assets)
        {
            var content = foldout.contentContainer.Children().ElementAt(1);
            var count = content.childCount;
            var size = assets.Count;
            for (var i = count; i < count + size; i++)
            {
                var slot = new AssetInventorySlot();
                var slotInfo = new SlotInfo(assets[i - count]);
                slot.SetSlotInfo(slotInfo);
                slot.SetSize(80);
                slot.SetGridPosition(i, 3);
                SetupFavoriteAction(slot);
                slot.VisualElement().userData = slot;
                content.Add(slot.VisualElement());
            }

            var total = size + count;
            GridUtils.SetContainerSize(content, total, 90, 3);
            foldout.contentContainer.style.height = content.style.height.value.value + 45;
        }

        private void SetupFavoriteAction(BaseInventorySlot slot)
        {
            var slotInfo = slot.GetSlotInfo();
            var isFavorite = IsUserFavorite(slotInfo, out _);
            slot.ConfigRightAction(isFavorite ? "Remove from favorites" : "Add to favorites",
                isFavorite ? removeFromFavoriteIcon : addToFavoriteIcon,
                () =>
                {
                    var isFavorite = IsUserFavorite(slotInfo, out _);
                    if (isFavorite)
                        RemoveFromFavorites(slot);
                    else
                        AddToFavorites(slot);
                });
            slot.SetRightActionVisible(true);
        }

        private bool IsUserFavorite(SlotInfo slotInfo, out FavoriteItem favoriteItem)
        {
            foreach (var item in favoriteItems)
            {
                if (item.asset != null &&
                    slotInfo.asset != null &&
                    item.asset.id.Value == slotInfo.asset.id.Value)
                {
                    favoriteItem = item;
                    return true;
                }

                if (
                    item.blockId != null &&
                    slotInfo.block != null &&
                    item.blockId.HasValue && item.blockId.Value == slotInfo.block.id)
                {
                    favoriteItem = item;
                    return true;
                }
            }

            favoriteItem = null;
            return false;
        }

        private void SetBodyContent(VisualElement body, VisualElement visualElement, Action onBack,
            string backButtonText = "Back")
        {
            body.Clear();
            if (onBack == null)
            {
                breadcrumb.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.None);
                body.Add(visualElement);
            }
            else
            {
                breadcrumb.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.Flex);
                var backButton = Utils.Utils.Create("Ui/AssetInventory/BackButton");
                backButton.Q<Label>("label").text = backButtonText;
                backButton.Q<Button>().clickable.clicked += onBack;
                backButton.style.width = 20 + backButtonText.Length * 10;
                breadcrumb.Clear();
                breadcrumb.Add(backButton);
            }

            body.Add(visualElement);
        }

        private Dictionary<int, List<Asset>> GroupAssetsByPack(List<Asset> assets)
        {
            var dictionary = new Dictionary<int, List<Asset>>();
            foreach (var asset in assets)
            {
                var pack = packs[asset.pack.id];
                if (!dictionary.ContainsKey(pack.id))
                    dictionary[pack.id] = new List<Asset>();
                dictionary[pack.id].Add(asset);
            }

            return dictionary;
        }

        public void AddToFavorites(BaseInventorySlot slot)
        {
            var slotInfo = slot.GetSlotInfo();
            var loadingId = LoadingLayer.LoadingLayer.Show(inventory);
            var favoriteItem = new FavoriteItem
            {
                asset = slotInfo.asset,
                blockId = slotInfo.block?.id
            };
            StartCoroutine(restClient.CreateFavoriteItem(favoriteItem, item =>
                {
                    LoadingLayer.LoadingLayer.Hide(loadingId);
                    favoriteItems.Add(item);
                    SetupFavoriteAction(slot);
                },
                () =>
                {
                    LoadingLayer.LoadingLayer.Hide(loadingId);
                    //TODO a toast?
                }));
        }

        public void RemoveFromFavorites(FavoriteItem favoriteItem, BaseInventorySlot slot, Action onDone = null)
        {
            var loadingId = LoadingLayer.LoadingLayer.Show(inventory);
            StartCoroutine(restClient.DeleteFavoriteItem(favoriteItem.id.Value,
                () =>
                {
                    LoadingLayer.LoadingLayer.Hide(loadingId);
                    favoriteItems.Remove(favoriteItem);
                    SetupFavoriteAction(slot);
                    onDone?.Invoke();
                    //TODO a toast?
                }, () =>
                {
                    LoadingLayer.LoadingLayer.Hide(loadingId);
                    //TODO a toast?
                }));
        }

        public void RemoveFromFavorites(BaseInventorySlot slot)
        {
            var slotInfo = slot.GetSlotInfo();
            if (IsUserFavorite(slotInfo, out var favoriteItem))
                RemoveFromFavorites(favoriteItem, slot);
            else
                throw new Exception("Trying to remove from favorites a slot that is not a favorite");
        }

        public void SelectSlot(InventorySlot slot, bool addToHandyPanel = true)
        {
            selectedSlot?.SetSelected(false);
            if (selectedSlot == slot || slot == null)
            {
                selectedSlot = null;
                selectedSlotChanged.Invoke(null);
                selectedHandySlotIndex = -1;
                return;
            }

            selectedSlot = slot;
            slot.SetSelected(true);
            var slotInfo = slot.GetSlotInfo();
            selectedSlotChanged.Invoke(slotInfo);
            if (!slotInfo.IsEmpty())
            {
                if (addToHandyPanel)
                    AddToHandyPanel(slotInfo);
                else // from handy bar itself
                {
                    for (var i = 0; i < handyBarSlots.Count; i++)
                    {
                        if (handyBarSlots[i] == slot)
                        {
                            selectedHandySlotIndex = i;
                            break;
                        }
                    }
                }
            }
            else
            {
                selectedHandySlotIndex = -1;
            }
        }

        public void SelectSlotInfo(SlotInfo slotInfo)
        {
            if (slotInfo == null)
            {
                SelectSlot(null);
            }
            else if (slotInfo.IsEmpty())
            {
                SelectSlot(hammerSlot);
            }
            else
            {
                foreach (var handyBarSlot in handyBarSlots)
                {
                    if (Equals(handyBarSlot.GetSlotInfo(), slotInfo))
                    {
                        SelectSlot(handyBarSlot);
                        break;
                    }
                }
            }
        }

        public void AddToHandyPanel(SlotInfo slotInfo)
        {
            foreach (var handyBarSlot in handyBarSlots)
            {
                if (handyBarSlot.GetSlotInfo().Equals(slotInfo))
                {
                    handyBarSlots.Remove(handyBarSlot);
                    handyBarSlots.Insert(0, handyBarSlot);
                    if (handyBar.Contains(handyBarSlot.VisualElement()))
                        handyBar.Remove(handyBarSlot.VisualElement());
                    else
                        handyBar.RemoveAt(handyBar.childCount - 1);
                    handyBar.Insert(0, handyBarSlot.VisualElement());
                    SelectSlot(handyBarSlot, false);
                    SaveHandySlots();
                    return;
                }
            }

            var slot = new HandyItemInventorySlot();
            slot.SetSize(70);
            slot.SetSlotInfo(slotInfo);
            if (handyBarSlots.Count == 15)
                handyBarSlots.RemoveAt(14);
            handyBarSlots.Insert(0, slot);
            handyBar.Insert(0, slot.VisualElement());
            if (handyBar.childCount > 10)
                handyBar.RemoveAt(handyBar.childCount - 1);
            SaveHandySlots();
            selectedHandySlotIndex = 0;
            SelectSlot(slot, false);
        }

        public void RemoveFromHandyPanel(InventorySlotWrapper slot)
        {
            handyBarSlots.Remove(slot);
            slot.VisualElement().RemoveFromHierarchy();
            while (handyBar.childCount < 10 && handyBarSlots.Count >= 10)
                handyBar.Add(handyBarSlots[handyBar.childCount].VisualElement());
            SaveHandySlots();
        }

        private void SaveHandySlots()
        {
            var items = handyBarSlots.Select(slot => SerializableSlotInfo.FromSlotInfo(slot.GetSlotInfo())).ToList();
            PlayerPrefs.SetString(HANDY_SLOTS_KEY, JsonConvert.SerializeObject(items));
        }

        private List<SlotInfo> GetSavedHandySlots()
        {
            return JsonConvert
                .DeserializeObject<List<SerializableSlotInfo>>(PlayerPrefs.GetString(HANDY_SLOTS_KEY, "[]"))
                .Select(serializedSlotInfo => serializedSlotInfo.ToSlotInfo()).ToList();
        }

        public SlotInfo GetSelectedSlot()
        {
            return selectedSlot.GetSlotInfo();
        }

        public static AssetsInventory INSTANCE => instance;

        public void ReloadTab()
        {
            tabPane.OpenTab(tabPane.GetCurrentTab());
        }
    }
}