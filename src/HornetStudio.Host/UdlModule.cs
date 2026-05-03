using System;
using Amium.Items;
using HornetStudio.Editor.Models;

namespace HornetStudio.Host
{
    internal sealed class UdlModule : Item
    {
        private const string RequestItemName = "Request";
        private const string ReadItemName = "Read";
        private const string SetItemName = "Set";
        private const string OutItemName = "Out";
        private const string StateItemName = "State";
        private const string AlertItemName = "Alert";
        private const string CommandItemName = "Command";

        public UdlModule(string name, string? path = null)
            : base(name, path: path)
        {
            Params["Kind"].Value = "UdlModule";
            Params["Text"].Value = name;
            Params["Unit"].Value = string.Empty;

            AddRequestChannel(ReadItemName);
            AddRequestChannel(SetItemName);
            AddRequestChannel(OutItemName);
            AddItem(StateItemName);
            AddItem(AlertItemName);
            AddRequestChannel(CommandItemName);
        }

        public Item Read => this[ReadItemName];
        public Item ReadRequest => Read[RequestItemName];
        public Item Set => this[SetItemName];
        public Item SetRequest => Set[RequestItemName];
        public Item Out => this[OutItemName];
        public Item OutRequest => Out[RequestItemName];
        public Item State => this[StateItemName];
        public Item Alert => this[AlertItemName];
        public Item Command => this[CommandItemName];
        public Item CommandRequest => Command[RequestItemName];

        public void EnsureWriteMetadata()
        {
            ApplyWriteMetadata(Read);
            ApplyWriteMetadata(Set);
            ApplyWriteMetadata(Out);
            ApplyWriteMetadata(Command);
        }

        private void AddRequestChannel(string name)
        {
            AddItem(name);
            var channel = this[name];
            channel.AddItem(RequestItemName);
            channel[RequestItemName].Params["Text"].Value = $"{name} Request";
            channel[RequestItemName].Value = channel.Value;
            ApplyWriteMetadata(channel);
        }

        private static void ApplyWriteMetadata(Item channel)
        {
            channel.Params["Writable"].Value = true;
            channel.Params["WritePath"].Value = channel.Path ?? string.Empty;
            channel.Params["WriteMode"].Value = SignalWriteMode.Request.ToString();
        }
    }
}
