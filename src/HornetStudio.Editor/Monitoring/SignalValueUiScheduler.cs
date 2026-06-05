using System;
using System.Collections.Generic;
using Avalonia.Threading;
using HornetStudio.Editor.Models;

namespace HornetStudio.Editor.Monitoring;

internal static class SignalValueUiScheduler
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, WeakReference<FolderItemModel>> Subscribers = new(StringComparer.Ordinal);
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);
    private static DispatcherTimer? _timer;

    internal static void Subscribe(FolderItemModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        lock (Sync)
        {
            Subscribers[item.SignalValueSchedulerSubscriptionKey] = new WeakReference<FolderItemModel>(item);
        }

        EnsureTimer();
    }

    internal static void Unsubscribe(FolderItemModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        lock (Sync)
        {
            Subscribers.Remove(item.SignalValueSchedulerSubscriptionKey);
            StopTimerIfIdleLocked();
        }
    }

    private static void EnsureTimer()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            EnsureTimerOnUiThread();
            return;
        }

        Dispatcher.UIThread.Post(EnsureTimerOnUiThread, DispatcherPriority.Background);
    }

    private static void EnsureTimerOnUiThread()
    {
        lock (Sync)
        {
            if (_timer is not null)
            {
                if (!_timer.IsEnabled)
                {
                    _timer.Start();
                }

                return;
            }

            _timer = new DispatcherTimer
            {
                Interval = TickInterval
            };
            _timer.Tick += OnTick;
            _timer.Start();
        }
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        List<string>? staleKeys = null;
        List<FolderItemModel> activeItems = [];

        lock (Sync)
        {
            foreach (var entry in Subscribers)
            {
                if (!entry.Value.TryGetTarget(out var item))
                {
                    staleKeys ??= [];
                    staleKeys.Add(entry.Key);
                    continue;
                }

                if (item.IsSignalValueSchedulerActive())
                {
                    activeItems.Add(item);
                }
            }

            if (staleKeys is not null)
            {
                foreach (var staleKey in staleKeys)
                {
                    Subscribers.Remove(staleKey);
                }
            }

            StopTimerIfIdleLocked();
        }

        if (activeItems.Count == 0)
        {
            return;
        }

        UiResponsivenessDiagnostics.RecordSignalPipelineEvent(stage: "SignalUiSchedulerTick");
        var tickUtc = DateTimeOffset.UtcNow;
        foreach (var item in activeItems)
        {
            item.ApplyScheduledSignalValueRefresh(tickUtc);
        }
    }

    private static void StopTimerIfIdleLocked()
    {
        if (_timer is null || Subscribers.Count != 0)
        {
            return;
        }

        _timer.Stop();
    }
}
