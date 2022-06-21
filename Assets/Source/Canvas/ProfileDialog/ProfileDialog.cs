using System.Collections.Generic;
using Source;
using Source.Canvas;
using Source.Model;
using Source.Service;
using Source.Ui.Menu;
using Source.Utils;
using TMPro;
using UnityEngine;

public class ProfileDialog : MonoBehaviour
{
    private static ProfileDialog instance;

    public TextMeshProUGUI nameLabel;
    public TextMeshProUGUI bioLabel;
    public ImageLoader profileImage;
    public GameObject socialLinks;
    public SocialLink socialLinkPrefab;
    public ActionButton closeButton;
    public GameObject editButton;
    private readonly List<GameObject> links = new List<GameObject>();
    private GameManager gameManager;

    void Start()
    {
        instance = this;
        gameObject.SetActive(false);
        gameManager = GameManager.INSTANCE;
        gameManager.stateGuards.Add(
            (currentState, nextState) =>
                !(gameObject.activeSelf && currentState == GameManager.State.PROFILE_DIALOG));
        closeButton.AddListener(CloseIfOpened);
        editButton.GetComponent<ActionButton>().AddListener(() => gameManager.EditProfile());
    }

    public void Open(Profile profile)
    {
        gameObject.SetActive(true);
        SetProfile(profile);
        gameManager.SetProfileDialogState(true);
    }

    public void CloseIfOpened()
    {
        if (!gameObject.activeSelf)
            return;
        nameLabel.SetText("");
        bioLabel.SetText("");
        profileImage.SetUrl(null);
        if (links.Count > 0)
        {
            foreach (var link in links) DestroyImmediate(link);
            links.Clear();
        }

        gameObject.SetActive(false);
        gameManager.SetProfileDialogState(false);
    }

    public void SetProfile(Profile profile)
    {
        if (profile == null)
        {
            nameLabel.SetText("No Profile Found");
            bioLabel.SetText("");
            profileImage.SetUrl(null);
            return;
        }

        if (profile.name != null)
            nameLabel.SetText(profile.name);
        if (profile.bio != null)
            bioLabel.SetText(profile.bio);
        if (profile.imageUrl != null)
            profileImage.SetUrl(Constants.ApiURL + "/profile/avatar/" + profile.imageUrl);
        if (profile.links != null)
        {
            foreach (var link in profile.links)
            {
                SocialLink socialLink = Instantiate(socialLinkPrefab, socialLinks.transform);
                socialLink.link = link.link;
                socialLink.media = link.GetMedia();
                links.Add(socialLink.gameObject);
            }
        }

        editButton.SetActive(profile.walletId.Equals(Settings.WalletId()));
    }

    public static ProfileDialog INSTANCE => instance;
}