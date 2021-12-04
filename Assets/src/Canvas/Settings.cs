using src.Model;
using src.Service;
using src.Service.Ethereum;
using UnityEngine;
using UnityEngine.UI;

namespace src.Canvas
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
        [SerializeField] private Button saveGameButton;
        [SerializeField] private Button editProfileButton;
        [SerializeField] private Button helpButton;

        void Start()
        {
            panel.SetActive(false);
            loadingPanel.SetActive(true);
            StartCoroutine(EthNetwork.GetNetworks(networks => DoStart(), () =>
            {
                errorPanel.SetActive(true);
                loadingPanel.SetActive(false);
            }));
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
            else
                panel.SetActive(true);

            foreach (var net in nets)
                networkInput.options.Add(new Dropdown.OptionData(net.name));

            ResetInputs();
            var manager = GameManager.INSTANCE;
            saveGameButton.onClick.AddListener(() => manager.Save());
            editProfileButton.onClick.AddListener(() => manager.ShowUserProfile());
            helpButton.onClick.AddListener(() => manager.Help());
            walletInput.onEndEdit.AddListener((text) => ResetButtonsState());

            manager.stateChange.AddListener(state =>
            {
                ResetInputs();
                gameObject.SetActive(state == GameManager.State.SETTINGS);
            });
            if (WebBridge.IsPresent())
            {
                WebBridge.CallAsync<ConnectionDetail>("connectMetamask", "", (ci) =>
                {
                    if (ci.network.HasValue && ci.wallet != null)
                    {
                        PlayerPrefs.SetInt(Keys.NETWORK, ci.network.Value);
                        PlayerPrefs.SetString(Keys.WALLET, ci.wallet);
                        ResetInputs();
                    }
                });
            }
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

            networkInput.interactable = !EthereumClientService.INSTANCE.IsInited();
            saveGameButton.interactable = !IsGuest() && VoxelService.INSTANCE.HasChange();
            editProfileButton.interactable = !IsGuest();

            saveGameButton.gameObject.SetActive(EthereumClientService.INSTANCE.IsInited());
            editProfileButton.gameObject.SetActive(EthereumClientService.INSTANCE.IsInited());
            helpButton.gameObject.SetActive(EthereumClientService.INSTANCE.IsInited());
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
                GameManager.INSTANCE.ExitSettings();
                return;
            }

            PlayerPrefs.SetString(Keys.WALLET, walletId);
            GameManager.INSTANCE.SettingsChanged(Network());
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
    }

    class Keys
    {
        public static readonly string WALLET = "WALLET";
        public static readonly string NETWORK = "NETWORK";
    }
}