using System.Text.RegularExpressions;

public class ConsoleMenu
{
    private readonly List<MenuItem> _menuItems = [];
    private bool _isRunning = true; 
    public void AddMenuItem(string text, Action action)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException("Menu item text cannot be null or empty.");
        }

        _menuItems.Add(new MenuItem(text, action));
    }

    public void AddExitMenuItem(string text = "Exit")
    {
        AddMenuItem(text, () => _isRunning = false);
    }

    public void DisplayMenu()
    {
        //Console.Clear(); // Clear the console for a clean presentation

        for (int i = 0; i < _menuItems.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {_menuItems[i].Text}");
        }
    }

    public void Run()
    {
        while (_isRunning)
        {
            DisplayMenu();

            int choice = GetValidUserChoice();

            if (choice > 0 && choice <= _menuItems.Count)
            {
                _menuItems[choice - 1].Action();
            }
            else
            {
                Console.WriteLine("Invalid choice. Please select a valid option.");
            }
        }
    }

    private static int GetValidUserChoice()
    {
        string input;
        int choice;

        do
        {
            Console.Write("Enter your choice: ");
            input = Console.ReadLine();
        } while (!int.TryParse(input, out choice));

        return choice;
    }

    public static object RequestVariable(string prompt, string dataType = "string", bool requireConfirmation = false)
    {
        while (true)
        {
            Console.Write($"\r{prompt}: ");
            var input = Console.ReadLine();
            object _result;
            if(dataType == "int" && int.TryParse(input, out var result)){
                _result = result;
            }
            else if (dataType == "string"){
                _result = input;
            }
            else
            {
                System.Console.WriteLine("\r Invalid input                                                                   ");
                continue;
            }
            
            if (!requireConfirmation)
            {
                return _result;
            }
            else
            {
                Console.Write($"\r                                                                                 ");
                Console.Write($"\r{prompt} is '{input}'. Confirm? (y/N): ");
                if (Console.ReadKey(false).KeyChar.ToString().Equals("y", StringComparison.InvariantCultureIgnoreCase))
                {
                    System.Console.WriteLine();
                    return _result;
                }
                System.Console.WriteLine();
            }
        }
    }


    private class MenuItem(string text, Action action)
    {
        public string Text { get; } = text;
        public Action Action { get; } = action;
    }
}
