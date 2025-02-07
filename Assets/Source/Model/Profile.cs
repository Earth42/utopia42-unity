﻿using System;
using System.Collections.Generic;
using UnityEngine;

namespace Source.Model
{
    [System.Serializable]
    public class Profile
    {
        public static readonly Profile LOADING_PROFILE = new Profile();
        public static readonly Profile FAILED_TO_LOAD_PROFILE = new Profile();

        static Profile()
        {
            LOADING_PROFILE.name = "Loading...";
            LOADING_PROFILE.avatarUrl = null;
            LOADING_PROFILE.walletId = "";
            LOADING_PROFILE.links = null;

            FAILED_TO_LOAD_PROFILE.name = "Failed to load";
            FAILED_TO_LOAD_PROFILE.avatarUrl = null;
            FAILED_TO_LOAD_PROFILE.walletId = "";
            FAILED_TO_LOAD_PROFILE.links = null;
        }

        public long citizenId;
        public string walletId;
        public string name;
        public string bio;
        public List<Link> links;
        public string avatarUrl;
        public List<Property> properties;
        
        public override int GetHashCode()
        {
            return walletId.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj == this) return true;
            if ((obj == null) || GetType() != obj.GetType())
                return false;
            var other = (Profile) obj;
            return walletId == other.walletId;
        }

        [Serializable]
        public class Property
        {
            public string key;
            public string value;
        }

        public class Link
        {
            public static readonly Dictionary<string, Media> medias = new()
            {
                {"TELEGRAM", Media.TELEGRAM},
                {"DISCORD", Media.DISCORD},
                {"FACEBOOK", Media.FACEBOOK},
                {"TWITTER", Media.TWITTER},
                {"INSTAGRAM", Media.INSTAGRAM},
                {"OTHER", Media.OTHER},
            };

            public string link;
            public string media;

            public Media GetMedia()
            {
                return medias[media];
            }

            public class Media
            {
                public static Media TELEGRAM = new Media(0, "Telegram");
                public static Media DISCORD = new Media(1, "Discord");
                public static Media FACEBOOK = new Media(2, "Facebook");
                public static Media TWITTER = new Media(3, "Twitter");
                public static Media INSTAGRAM = new Media(4, "Instagram");
                public static Media OTHER = new Media(5, "Link");

                private int index;
                private string name;

                Media(int index, string name)
                {
                    this.index = index;
                    this.name = name;
                }

                public int GetIndex()
                {
                    return index;
                }

                public string GetName()
                {
                    return name;
                }

                public Sprite GetIcon()
                {
                    return Resources.Load<Sprite>("Icons/Media/" + name);
                }
            }
        }
    }
}