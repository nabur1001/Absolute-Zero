namespace AbsoluteZero.Core.Session
{
    public enum OperationErrorCode
    {
        None,
        Cancelled,
        InvalidState,
        AuthenticationFailed,
        LobbyNotFound,
        LobbyConflict,
        RelayFailed,
        NetworkStartFailed,
        Timeout,
        Unexpected
    }

    public readonly struct Unit
    {
        public static readonly Unit Value = default;
    }

    public readonly struct Result<T>
    {
        public T Value { get; }
        public OperationErrorCode ErrorCode { get; }
        public string ErrorMessage { get; }
        public bool IsSuccess => ErrorCode == OperationErrorCode.None;
        public bool IsFailure => !IsSuccess;

        private Result(T value)
        {
            Value = value;
            ErrorCode = OperationErrorCode.None;
            ErrorMessage = null;
        }

        private Result(OperationErrorCode errorCode, string errorMessage)
        {
            Value = default;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        public static Result<T> Success(T value) => new(value);
        public static Result<T> Failure(OperationErrorCode code, string message) => new(code, message);
    }
}
