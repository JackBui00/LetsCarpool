using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LetsCarpool.Server.Controllers
{
    public class RideHistoryController : Controller
    {
        // GET: RideHistoryController
        public ActionResult Index()
        {
            return View();
        }

        // GET: RideHistoryController/Details/5
        public ActionResult Details(int id)
        {
            return View();
        }

        // GET: RideHistoryController/Create
        public ActionResult Create()
        {
            return View();
        }

        // POST: RideHistoryController/Create
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

        // GET: RideHistoryController/Edit/5
        public ActionResult Edit(int id)
        {
            return View();
        }

        // POST: RideHistoryController/Edit/5
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

        // GET: RideHistoryController/Delete/5
        public ActionResult Delete(int id)
        {
            return View();
        }

        // POST: RideHistoryController/Delete/5
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
