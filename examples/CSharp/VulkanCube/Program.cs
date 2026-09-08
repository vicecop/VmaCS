using System;

namespace VulkanCube;

public class Program
{
    private static void Main()
    {
        try
        {
            var app = new DrawCubeExample();

            app.Run();

            app.Dispose();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);

            if (e is VmaCS.VulkanResultException ve)
            {
                Console.WriteLine("\nResult Code: " + ve.Result);
            }
        }
    }

}
