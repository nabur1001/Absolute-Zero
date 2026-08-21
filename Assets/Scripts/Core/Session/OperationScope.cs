using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace AbsoluteZero.Core.Session
{
    public sealed class OperationScope
    {
        readonly Func<uint> _getGeneration;
        readonly uint _captured;
        readonly List<Func<Task>> _compensations = new();

        public bool IsStale => _getGeneration() != _captured;

        public OperationScope(Func<uint> getGeneration)
        {
            _getGeneration = getGeneration;
            _captured = getGeneration();
        }

        public void PushCompensation(Func<Task> action) => _compensations.Add(action);

        public async Task<Result<Unit>> CancelWithCompensation()
        {
            for (int i = _compensations.Count - 1; i >= 0; i--)
            {
                try { await _compensations[i](); }
                catch (Exception e) { Debug.LogWarning($"[OperationScope] Compensation failed: {e.Message}"); }
            }
            _compensations.Clear();
            return Result<Unit>.Failure(OperationErrorCode.Cancelled, "Operation superseded by newer session operation");
        }

        public async Task RunCompensations()
        {
            for (int i = _compensations.Count - 1; i >= 0; i--)
            {
                try { await _compensations[i](); }
                catch (Exception e) { Debug.LogWarning($"[OperationScope] Compensation failed: {e.Message}"); }
            }
            _compensations.Clear();
        }
    }
}
