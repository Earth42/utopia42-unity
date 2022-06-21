using Source.Model;
using Source.Service;
using Source.Service.Ethereum;
using UnityEngine;
using UnityEngine.UI;

namespace Source.Canvas
{
    public class Settings : MonoBehaviour
    {
        private static readonly string GUEST = "guest";
        [SerializeField] private GameObject panel;
        [SerializeField] private GameObject errorPanel;
        [SerializeField] private GameObject loadingPanel;
        [SerializeField] private InputField walletInput;
        [SerializeField] private Dropdown networkInput;
        [SerializeField] private Button submitButton;
        [SerializeField] private Button guestButton;
        [SerializeField] private Button saveGameButton;
        [SerializeField] private Button editProfileButton;
        [SerializeField] private Button helpButton;
        [SerializeField] private Button positionLinkButton;
        [SerializeField] private Button pluginsButton;

        private Vector3? startingPosition = null;

        void Start()
        {
            panel.SetActive(false);
            loadingPanel.SetActive(true);
            StartCoroutine(EthNetwork.GetNetworks(networks => DoStart(), () =>
            {
                errorPanel.SetActive(true);
                loadingPanel.SetActive(false);
            }));
            pluginsButton.interactable = WebBridge.IsPresent();
        }

        private void DoStart()
        {
            loadingPanel.SetActive(false);
            var nets = EthNetwork.GetNetworksIfPresent();
            if (nets == null || nets.Length == 0)
            {
                errorPanel.SetActive(true);
                return;
            }

            panel.SetActive(true);

            foreach (var net in nets)
                networkInput.options.Add(new Dropdown.OptionData(net.name));

            ResetInputs();
            var manager = GameManager.INSTANCE;
            saveGameButton.onClick.AddListener(() => manager.Save());
            editProfileButton.onClick.AddListener(() => manager.ShowUserProfile());
            helpButton.onClick.AddListener(() => manager.OpenHelpDialog());
            walletInput.onEndEdit.AddListener((text) => ResetButtonsState());
            positionLinkButton.onClick.AddListener(() => manager.CopyPositionLink());
            pluginsButton.onClick.AddListener(() => manager.OpenPluginsDialog());

            manager.stateChange.AddListener(state =>
            {
                ResetInputs();
                gameObject.SetActive(state == GameManager.State.SETTINGS);
            });

            if (!WebBridge.IsPresent()) return;

            WebBridge.CallAsync<ConnectionDetail>("connectMetamask", "", (ci) =>
            {
                if (ci.network.HasValue && ci.wallet != null)
                {
                    PlayerPrefs.SetInt(Keys.NETWORK, ci.network.Value);
                    PlayerPrefs.SetString(Keys.WALLET, ci.wallet);
                    ResetInputs();
                }
            });

            guestButton.interactable = false;
            submitButton.interactable = false;
            WebBridge.CallAsync<Position>("getStartingPosition", "", (pos) =>
            {
                if (pos == null)
                    startingPosition = null;
                else
                    startingPosition = new Vector3(pos.x, pos.y, pos.z);
                guestButton.interactable = true;
                submitButton.interactable = true;
            });
        }

        private void ResetInputs()
        {
            walletInput.text = IsGuest() ? null : WalletId();
            var net = Network();
            networkInput.value = -1;

            var nets = EthNetwork.GetNetworksIfPresent();
            for (int i = 0; i < networkInput.options.Count; i++)
            {
                if (nets[i].Equals(net))
                {
                    networkInput.value = i;
                    break;
                }
            }

            bool serviceInitialized = EthereumClientService.INSTANCE.IsInited();
            networkInput.interactable = !serviceInitialized;
            saveGameButton.interactable = !IsGuest() && WorldService.INSTANCE.HasChange();
            editProfileButton.interactable = !IsGuest();

            saveGameButton.gameObject.SetActive(serviceInitialized);
            editProfileButton.gameObject.SetActive(serviceInitialized);
            helpButton.gameObject.SetActive(serviceInitialized);
            pluginsButton.gameObject.SetActive(serviceInitialized);
            positionLinkButton.gameObject.SetActive(serviceInitialized);
        }

        private void ResetButtonsState()
        {
            submitButton.interactable = !string.IsNullOrEmpty(walletInput.text);
        }

        void Update()
        {
            ResetButtonsState();
        }

        public void SetGuest()
        {
            DoSubmit(GUEST);
        }

        public void Submit()
        {
            if (string.IsNullOrWhiteSpace(walletInput.text)) return;
            DoSubmit(walletInput.text);
        }


        private void DoSubmit(string walletId)
        {
            if (networkInput.interactable)
                PlayerPrefs.SetInt(Keys.NETWORK, EthNetwork.GetNetworksIfPresent()[networkInput.value].id);
            else if (Equals(WalletId(), walletId))
            {
                GameManager.INSTANCE.ExitSettings(startingPosition);
                return;
            }

            PlayerPrefs.SetString(Keys.WALLET, walletId);
            GameManager.INSTANCE.SettingsChanged(Network(), startingPosition);
        }

        public static bool IsGuest()
        {
            return WalletId().Equals(GUEST);
        }

        public static string WalletId()
        {
            return PlayerPrefs.GetString(Keys.WALLET).ToLower();
        }

        private static EthNetwork Network()
        {
            var nets = EthNetwork.GetNetworksIfPresent();
            return EthNetwork.GetByIdIfPresent(PlayerPrefs.GetInt(Keys.NETWORK,
                nets == null || nets.Length == 0 ? -1 : nets[0].id));
        }

        public static ConnectionDetail ConnectionDetail()
        {
            var detail = new ConnectionDetail();
            detail.wallet = WalletId();
            var net = Network();
            detail.network = net == null ? -1 : net.id;
            return detail;
        }

        public void Exit()
        {
            if (WebBridge.IsPresent())
            {
                WebBridge.Call<object>("moveToHome", null);
            }
            else
            {
                Application.Quit();
            }
        }
    }
}