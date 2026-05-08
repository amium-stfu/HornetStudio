using System;
using ItemModel = Amium.Items.Item;
using Amium.Items;
using HornetStudio.Editor.Models;

namespace HornetStudio.Host
{
    internal sealed class UdlModule : ItemModel
    {
        private const string ReadItemName = "Read";
        private const string SetItemName = "Set";
        private const string OutItemName = "Out";
        private const string StateItemName = "State";
        private const string AlertItemName = "Alert";

        public UdlModule(string name, string? path = null)
            : base(name, path: path)
        {
            Properties["kind"].Value = "UdlModule";
            Properties["text"].Value = name;
            Properties["unit"].Value = string.Empty;

            AddChannel(ReadItemName, hasWriteChannel: true);
            AddChannel(SetItemName, hasWriteChannel: true);
            AddChannel(OutItemName, hasWriteChannel: true);
            AddChannel(StateItemName, hasWriteChannel: true);
            AddChannel(AlertItemName, hasReadChannel: false);
        }

        public ItemModel Read => this[ReadItemName];
        public ItemModel Set => this[SetItemName];
        public ItemModel Out => this[OutItemName];
        public ItemModel State => this[StateItemName];
        public ItemModel Alert => this[AlertItemName];

        public void EnsureWriteMetadata()
        {
            EnsureWriteChannel(Read);
            EnsureWriteChannel(Set);
            EnsureWriteChannel(Out);
            EnsureWriteChannel(State);
            EnsureNoReadChannel(Alert);
            RemoveLegacyCommand();
        }

        private void AddChannel(string name, bool hasWriteChannel = false, bool hasReadChannel = true)
        {
            this[name] = new ItemModel(
                name,
                path: Path,
                hasWriteChannel: hasWriteChannel,
                hasReadChannel: hasReadChannel);
        }

        private static void EnsureWriteChannel(ItemModel channel)
        {
            if (!channel.Properties.Has("write"))
            {
                channel.Properties["write"].Value = channel.Properties.Has("read")
                    ? channel.Properties["read"].Value
                    : null!;
            }
        }

        private static void EnsureNoReadChannel(ItemModel channel)
        {
            if (!channel.Properties.Has("read"))
            {
                return;
            }

            if (channel.Properties["read"].Value is not null && !channel.Properties.Has("value"))
            {
                channel.Properties["value"].Value = channel.Properties["read"].Value;
            }

            channel.Properties.Remove("read");
            channel.Properties.Remove("write");
        }

        private void RemoveLegacyCommand()
        {
            if (Has("Command"))
            {
                Remove("Command");
            }
        }
    }
}
