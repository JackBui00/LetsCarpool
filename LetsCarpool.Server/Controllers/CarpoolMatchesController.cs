using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LetsCarpool.Server.Controllers
{
    public class CarpoolMatchesController : Controller
    {
        // GET: CarpoolMatchesController
        public ActionResult Index()
        {
            return View();
        }

        // GET: CarpoolMatchesController/Details/5
        public ActionResult Details(int id)
        {
            return View();
        }

        // GET: CarpoolMatchesController/Create
        public ActionResult Create()
        {
            return View();
        }

        // POST: CarpoolMatchesController/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: CarpoolMatchesController/Edit/5
        public ActionResult Edit(int id)
        {
            return View();
        }

        // POST: CarpoolMatchesController/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: CarpoolMatchesController/Delete/5
        public ActionResult Delete(int id)
        {
            return View();
        }

        // POST: CarpoolMatchesController/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }
    }
}
