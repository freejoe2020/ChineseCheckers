using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI; // Required for Toggle component access

/// <summary>
/// Manages Toggle component state changes with associated UnityEvents
/// Triggers custom actions when toggle is checked/unchecked
/// Includes proper event cleanup to prevent memory leaks
/// </summary>
public class TQ_ToggleAnimation : MonoBehaviour
{
    /// <summary>
    /// Event triggered when toggle is checked (ON state)
    /// Assign custom actions via Inspector or code
    /// </summary>
    public UnityEvent onAction;

    /// <summary>
    /// Event triggered when toggle is unchecked (OFF state)
    /// Assign custom actions via Inspector or code
    /// </summary>
    public UnityEvent offAction;

    /// <summary>
    /// Reference to the Toggle component on this GameObject
    /// </summary>
    private Toggle _toggle;

    /// <summary>
    /// Initializes component references and event listeners on first frame
    /// </summary>
    void Start()
    {
        // Get Toggle component from current GameObject (can also be assigned manually in Inspector)
        _toggle = GetComponent<Toggle>();

        // Safety check: Log error and exit if Toggle component is missing
        if (_toggle == null)
        {
            Debug.LogError("Toggle component not found on this GameObject! Please check the GameObject setup.", this);
            return;
        }

        // Subscribe to toggle value change event
        _toggle.onValueChanged.AddListener(OnToggleValueChanged);

        // Initialize with current toggle state
        if (_toggle.isOn)
        {
            OnAction();
        }
        else
        {
            OffAction();
        }
    }

    /// <summary>
    /// Callback method for toggle state changes
    /// Triggers appropriate action based on new state
    /// </summary>
    /// <param name="isOn">New state of the toggle (true = checked, false = unchecked)</param>
    private void OnToggleValueChanged(bool isOn)
    {
        if (isOn)
        {
            // Execute ON actions when toggle is checked
            OnAction();
        }
        else
        {
            // Execute OFF actions when toggle is unchecked
            OffAction();
        }
    }

    /// <summary>
    /// Executes actions for toggle ON state
    /// Invokes the onAction UnityEvent
    /// </summary>
    private void OnAction()
    {
        Debug.Log("Toggle is checked, executing OnAction");
        onAction?.Invoke(); // Safe invocation (null check)
    }

    /// <summary>
    /// Executes actions for toggle OFF state
    /// Invokes the offAction UnityEvent
    /// Modify internal logic as needed for specific use cases
    /// </summary>
    private void OffAction()
    {
        Debug.Log("Toggle is unchecked, executing OffAction");
        offAction?.Invoke(); // Safe invocation (null check)
    }

    /// <summary>
    /// Cleans up event listeners to prevent memory leaks
    /// Called when GameObject is destroyed
    /// </summary>
    private void OnDestroy()
    {
        // Remove listener only if Toggle reference exists
        if (_toggle != null)
        {
            _toggle.onValueChanged.RemoveListener(OnToggleValueChanged);
        }
    }
}