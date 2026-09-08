namespace VmaCS;

internal sealed class MappingHysteresis
{
    // VMA_MAPPING_HYSTERESIS_ENABLED is always 1 in this port.
    private const int COUNTER_MIN_EXTRA_MAPPING = 7;

    private uint _minorCounter;
    private uint _majorCounter;
    private uint _extraMapping; // 0 or 1

    public uint ExtraMapping => _extraMapping;

    // Call when Map was called.
    // Returns true if switched to extra +1 mapping reference count.
    public bool PostMap()
    {
        if (_extraMapping == 0)
        {
            _majorCounter++;

            if (_majorCounter >= COUNTER_MIN_EXTRA_MAPPING)
            {
                _extraMapping = 1;
                _majorCounter = 0;
                _minorCounter = 0;
                return true;
            }
        }
        else // _extraMapping == 1
        {
            PostMinorCounter();
        }

        return false;
    }

    // Call when Unmap was called.
    public void PostUnmap()
    {
        if (_extraMapping == 0)
        {
            _majorCounter++;
        }
        else // _extraMapping == 1
        {
            PostMinorCounter();
        }
    }

    // Call when allocation was made from the memory block.
    public void PostAlloc()
    {
        if (_extraMapping == 1)
        {
            _majorCounter++;
        }
        else // _extraMapping == 0
        {
            PostMinorCounter();
        }
    }

    // Call when allocation was freed from the memory block.
    // Returns true if switched to extra -1 mapping reference count.
    public bool PostFree()
    {
        if (_extraMapping == 1)
        {
            _majorCounter++;

            if (_majorCounter >= COUNTER_MIN_EXTRA_MAPPING && _majorCounter > _minorCounter + 1)
            {
                _extraMapping = 0;
                _majorCounter = 0;
                _minorCounter = 0;
                return true;
            }
        }
        else // _extraMapping == 0
        {
            PostMinorCounter();
        }

        return false;
    }

    private void PostMinorCounter()
    {
        if (_minorCounter < _majorCounter)
        {
            _minorCounter++;
        }
        else if (_majorCounter > 0)
        {
            _majorCounter--;
            _minorCounter--;
        }
    }
}
