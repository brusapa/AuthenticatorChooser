using NLog;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Input;

namespace AuthenticatorChooser;

public static class SecurityKeyChooser {

    // #4: unfortunately, this class name is shared with the UAC prompt, detectable when desktop dimming is disabled
    private const string WINDOW_CLASS_NAME  = "Credential Dialog Xaml Host";

    private static readonly Logger LOGGER = LogManager.GetCurrentClassLogger();

    public static void chooseUsbSecurityKey(AutomationElement fidoEl)
    {
        try
        {
            AutomationElement? outerScrollViewer = fidoEl.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ClassNameProperty, "ScrollViewer"));
            if (outerScrollViewer?.FindFirst(TreeScope.Children, new AndCondition(
                    new PropertyCondition(AutomationElement.ClassNameProperty, "TextBlock"),
                    singletonSafePropertyCondition(AutomationElement.NameProperty, false, I18N.getStrings(I18N.Key.SIGN_IN_WITH_YOUR_PASSKEY)))) == null)
            { // #4
                LOGGER.Debug("Window is not a passkey choice prompt");
                return;
            }

            var authenticatorChoices = RetrieveCredentialList(fidoEl, TimeSpan.FromMinutes(1));

            IEnumerable<string> securityKeyLabelPossibilities = I18N.getStrings(I18N.Key.SECURITY_KEY);
            AutomationElement? securityKeyChoice = authenticatorChoices.FirstOrDefault(choice => nameContainsAny(choice, securityKeyLabelPossibilities));
            if (securityKeyChoice == null)
            {
                LOGGER.Debug("USB security key is not a choice, skipping");
                return;
            }

            ((SelectionItemPattern)securityKeyChoice.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            LOGGER.Info("USB security key selected");

            AutomationElement nextButton = fidoEl.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.AutomationIdProperty, "OkButton"))!;
            if (KeyboardHelper.IsShiftPressed())
            {
                nextButton.SetFocus();
                LOGGER.Info("Shift is pressed, not submitting dialog box");
                return;
            }
            
            if (!authenticatorChoices.All(choice => choice == securityKeyChoice || nameContainsAny(choice, I18N.getStrings(I18N.Key.SMARTPHONE))))
            {
                nextButton.SetFocus();
                LOGGER.Info("Dialog box has a choice that is neither pairing a new phone nor USB security key (such as an existing phone, PIN, or biometrics), " +
                    "skipping because the user might want to choose it");
                return;
            }

            ((InvokePattern)nextButton.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            LOGGER.Info("Next button pressed");
        }
        catch (TimeoutException e)
        {
            LOGGER.Error(e, e.Message);
        }
        catch (COMException e)
        {
            LOGGER.Warn(e, "UI Automation error while selecting security key, skipping this dialog box instance");
        }
    }

    // Window name and title are localized, so don't match against those
    public static bool isFidoPromptWindow(AutomationElement element) => element.Current.ClassName == WINDOW_CLASS_NAME;

    private static bool nameContainsAny(AutomationElement element, IEnumerable<string?> possibleSubstrings) {
        string name = element.Current.Name;
        // #2: in addition to a prefix, there is sometimes also a suffix after the substring
        return possibleSubstrings.Any(possibleSubstring => possibleSubstring != null && name.Contains(possibleSubstring, StringComparison.CurrentCulture));
    }

    /// <summary>
    /// <para>Create an <see cref="AndCondition"/> or <see cref="OrCondition"/> for a <paramref name="property"/> from a series of <paramref name="values"/>, which have fewer than 2 items in it.</para>
    /// <para>This avoids a crash in the <see cref="AndCondition"/> and <see cref="OrCondition"/> constructors if the array has size 1.</para>
    /// </summary>
    /// <param name="property">The name of the UI property to match against, such as <see cref="AutomationElement.NameProperty"/> or <see cref="AutomationElement.AutomationIdProperty"/>.</param>
    /// <param name="and"><c>true</c> to make a conjunction (AND), <c>false</c> to make a disjunction (OR)</param>
    /// <param name="values">Zero or more property values to match against.</param>
    /// <returns>A <see cref="Condition"/> that matches the values against the property, without throwing an <see cref="ArgumentException"/> if <paramref name="values"/> has length &lt; 2.</returns>
    private static Condition singletonSafePropertyCondition(AutomationProperty property, bool and, IEnumerable<string> values) {
        Condition[] propertyConditions = values.Select<string, Condition>(allowedValue => new PropertyCondition(property, allowedValue)).ToArray();
        return propertyConditions.Length switch {
            0 => and ? Condition.TrueCondition : Condition.FalseCondition,
            1 => propertyConditions[0],
            _ => and ? new AndCondition(propertyConditions) : new OrCondition(propertyConditions)
        };
    }

    /// <summary>
    /// Retrieve a enumerable of <see cref="AutomationElement"/> representing the list of authenticator choices in a FIDO prompt.
    /// </summary>
    /// <param name="fidoElement">FIDO prompt <see cref="AutomationElement"/></param>
    /// <param name="timeout">Maximum time to wait for the window to be initialized</param>
    /// <returns>A enumerable of <see cref="AutomationElement"/> representing the list of authenticator choices in a FIDO prompt.</returns>
    /// <exception cref="TimeoutException"></exception>
    private static IEnumerable<AutomationElement> RetrieveCredentialList(AutomationElement fidoElement, TimeSpan timeout)
    {
        var authenticatorChoicesStopwatch = Stopwatch.StartNew();
        while (authenticatorChoicesStopwatch.Elapsed < timeout)
        {
            var credentialListElement = fidoElement.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "CredentialsList"));
            if (credentialListElement != null)
            {
                var authenticatorChoices = credentialListElement
                                            .FindAll(TreeScope.Children, Condition.TrueCondition)
                                            .Cast<AutomationElement>();
                return authenticatorChoices;
            }
            Thread.Sleep(5);
        }
        throw new TimeoutException($"Could not find authenticator choices after retrying for {timeout} due to the following exception. Giving up and not automatically selecting Security Key.");
    }

}