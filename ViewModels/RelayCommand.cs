using System;
using System.Windows.Input;

namespace MediaArchiver.ViewModels
{
    /// <summary>
    /// 경량 ICommand 구현체.
    /// CanExecuteChanged 는 CommandManager 에 위임해 자동 재평가합니다.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute    = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter)
            => _canExecute == null || _canExecute(parameter);

        public void Execute(object? parameter)
            => _execute(parameter);

        /// <summary>강제로 CanExecute 재평가를 트리거합니다.</summary>
        public static void Refresh()
            => CommandManager.InvalidateRequerySuggested();
    }
}
