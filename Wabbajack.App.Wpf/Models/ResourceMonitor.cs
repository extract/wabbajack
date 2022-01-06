using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Subjects;
using System.Timers;
using DynamicData;
using DynamicData.Kernel;
using ReactiveUI;
using Wabbajack.RateLimiter;

namespace Wabbajack.Models;

public class ResourceMonitor : IDisposable
{
    private readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1000);
    
    private readonly IResource[] _resources;
    private readonly Timer _timer;
    
    private readonly Subject<(string Name, long Througput)[]> _updates = new ();
    private (string Name, long Throughput)[] _prev;
    public IObservable<(string Name, long Throughput)[]> Updates => _updates;


    private readonly SourceCache<CPUDisplayVM, ulong> _tasks = new(x => x.ID);
    public readonly ReadOnlyObservableCollection<CPUDisplayVM> _tasksFiltered;
    private readonly CompositeDisposable _compositeDisposable;
    public ReadOnlyObservableCollection<CPUDisplayVM> Tasks => _tasksFiltered;
    
    


    public ResourceMonitor(IEnumerable<IResource> resources)
    {
        _compositeDisposable = new CompositeDisposable();
        _resources = resources.ToArray();
        _prev = _resources.Select(x => (x.Name, (long)0)).ToArray();
        
        RxApp.MainThreadScheduler.ScheduleRecurringAction(PollInterval, Elapsed)
            .DisposeWith(_compositeDisposable);
        
        _tasks.Connect()
            .Bind(out _tasksFiltered)
            .Subscribe()
            .DisposeWith(_compositeDisposable);
    }

    private void Elapsed()
    {
        var current = _resources.Select(x => (x.Name, x.StatusReport.Transferred)).ToArray();
        var diff = _prev.Zip(current)
            .Select(t => (t.First.Name, (long)((t.Second.Transferred - t.First.Throughput) / PollInterval.TotalSeconds)))
            .ToArray();
        _prev = current;
        _updates.OnNext(diff);

        _tasks.Edit(l =>
        {
            var used = new HashSet<ulong>();
            foreach (var resource in _resources)
            {
                foreach (var job in resource.Jobs)
                {
                    used.Add(job.ID);
                    var tsk = l.Lookup(job.ID);
                    // Update
                    if (tsk != Optional<CPUDisplayVM>.None)
                    {
                        var t = tsk.Value;
                        t.Msg = job.Description;
                        t.ProgressPercent = job.Size == 0 ? Percent.Zero : Percent.FactoryPutInRange(job.Current, (long)job.Size);
                    }

                    // Create
                    else
                    {
                        var vm = new CPUDisplayVM
                        {
                            ID = job.ID,
                            StartTime = DateTime.Now,
                            Msg = job.Description,
                            ProgressPercent = job.Size == 0 ? Percent.Zero : Percent.FactoryPutInRange(job.Current, (long) job.Size)
                        };
                        l.AddOrUpdate(vm);
                    }
                }
            }
            
            // Delete
            foreach (var itm in l.Items.Where(v => !used.Contains(v.ID)))
                l.Remove(itm);
        });
    }

    public void Dispose()
    {
        _compositeDisposable.Dispose();
    }
}