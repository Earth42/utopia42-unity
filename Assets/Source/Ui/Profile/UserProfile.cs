using System.Collections.Generic;
using Source.Canvas;
using Source.Ui.AssetsInventory;
using Source.Ui.Map;
using Source.Ui.Menu;
using Source.Ui.Utils;
using Source.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace Source.Ui.Profile
{
    public class UserProfile : UxmlElement
    {
        private static readonly Sprite emptyUserIcon = Resources.Load<Sprite>("Icons/empty-user-icon");
        private VisualElement imageElement;
        private Label nameLabel;
        private ScrollView body;

        public UserProfile(Model.Profile profile) : base("Ui/Profile/UserProfile", true)
        {
            SetProfile(profile);
        }

        public void SetProfile(Model.Profile profile)
        {
            if (profile == null)
                return;

            imageElement = this.Q<VisualElement>("profileImage");
            if (profile.imageUrl != null)
            {
                var url = Constants.ApiURL + "/profile/avatar/" + profile.imageUrl;
                GameManager.INSTANCE.StartCoroutine(
                    UiImageUtils.SetBackGroundImageFromUrl(url, emptyUserIcon, imageElement));
            }

            nameLabel = this.Q<Label>("name");
            if (profile.name != null)
                nameLabel.text = profile.name;

            body = this.Q<ScrollView>("body");
            Utils.Utils.IncreaseScrollSpeed(body, 600);
            body.Clear();
            if (profile.bio != null)
            {
                var bioLabel = new Label
                {
                    text = profile.bio,
                    style =
                    {
                        flexWrap = new StyleEnum<Wrap>(Wrap.Wrap),
                        width = new StyleLength(new Length(100f, LengthUnit.Percent)),
                        whiteSpace = WhiteSpace.Normal,
                    }
                };
                body.Add(bioLabel);
            }

            if (profile.links == null)
            {
                profile.links = new List<Model.Profile.Link>();
                foreach (var media in Model.Profile.Link.medias)
                    profile.links.Add(new Model.Profile.Link
                    {
                        media = media.Key,
                        link = null
                    });
            }

            var socialLinks = this.Q<VisualElement>("socialLinks");
            for (var index = 0; index < profile.links.Count; index++)
            {
                var link = profile.links[index];
                var socialLink = new SocialLink(link);
                socialLinks.Add(socialLink);
                GridUtils.SetChildPosition(socialLink, 120, 40, index, 6);
            }

            GridUtils.SetContainerSize(socialLinks, profile.links.Count, 40, 6);

            var editButton = this.Q<Button>("userEditButton");
            if (profile.walletId.Equals(AuthService.WalletId()))
            {
                editButton.clickable.clicked += () => BrowserConnector.INSTANCE.EditProfile(() =>
                {
                    ProfileLoader.INSTANCE.InvalidateProfile(profile.walletId);
                    ProfileLoader.INSTANCE.load(profile.walletId, SetProfile, () =>
                    {
                        //FIXME Show error snack
                    });
                }, () => { });
            }
            else
            {
                editButton.style.display = DisplayStyle.None;
            }
        }
    }
}