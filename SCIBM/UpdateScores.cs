using System;
using System.Linq;
using SCIBM.Models;

namespace UpdateScores
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                using (var db = new ScibmContext())
                {
                    var preguntasZero = db.Preguntas.Where(p => p.Puntaje <= 0.0).ToList();
                    foreach (var p in preguntasZero)
                    {
                        p.Puntaje = 1.0;
                    }
                    db.SaveChanges();
                    Console.WriteLine($"Actualizadas {preguntasZero.Count} preguntas a 1.0 punto.");
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}
