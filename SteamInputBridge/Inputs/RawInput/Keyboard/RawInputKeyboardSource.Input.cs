namespace SteamInputBridge.Inputs.RawInput;

internal sealed partial class RawInputKeyboardSource
{
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSystemKeyDown = 0x0104;
    private const int WmSystemKeyUp = 0x0105;

    private readonly RawInputBuffer<RawInputNative.RawInputKeyboardData> _inputBuffer = new();

    private void HandleRawInput(nint rawInputHandle)
    {
        if (_inputBuffer.TryReadData(rawInputHandle, out RawInputNative.RawInputKeyboardData rawInput))
        {
            HandleRawInputEvent(rawInput);
        }

        _inputBuffer.Drain(HandleRawInputEvent);
    }

    private void HandleRawInputEvent(RawInputNative.RawInputKeyboardData rawInput)
    {
        if (rawInput.Header.Type != RawInputNative.RawInputKeyboard)
        {
            return;
        }

        uint message = rawInput.Keyboard.Message;
        if (message is WmKeyDown or WmSystemKeyDown)
        {
            _keyChanged(rawInput.Keyboard.VirtualKey, true);
        }
        else if (message is WmKeyUp or WmSystemKeyUp)
        {
            _keyChanged(rawInput.Keyboard.VirtualKey, false);
        }
    }

    private void FreeInputBuffer()
    {
        _inputBuffer.Dispose();
    }
}
