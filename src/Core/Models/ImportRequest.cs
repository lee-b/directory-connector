﻿using Bit.Core.Services;
using System.Collections.Generic;
using System.Linq;

namespace Bit.Core.Models
{
    public class ImportRequest
    {
        public ImportRequest(List<GroupEntry> groups, List<UserEntry> users)
        {
            Groups = groups?.Select(g => new Group(g)).ToArray() ?? new Group[] { };
            Users = users?.Select(u => new User(u)).ToArray() ?? new User[] { };
        }

        public Group[] Groups { get; set; }
        public User[] Users { get; set; }

        public class Group
        {
            public Group(GroupEntry entry)
            {
                Name = entry.Name;
                ExternalId = entry.ExternalId;
                Users = entry.UserMemberExternalIds;
            }

            public string Name { get; set; }
            public string ExternalId { get; set; }
            public IEnumerable<string> Users { get; set; }
        }

        public class User
        {
            public User(UserEntry entry)
            {
                Email = entry.Email;
                Deleted = (SettingsService.Instance.Sync.RemoveDisabledUsers && entry.Disabled) || entry.Deleted;
                ExternalId = entry.ExternalId;
            }

            public string ExternalId { get; set; }
            public string Email { get; set; }
            public bool Deleted { get; set; }
        }
    }

}
