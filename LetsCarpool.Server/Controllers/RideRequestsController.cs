using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LetsCarpool.Server.Controllers
{
    public class RideRequestsController : Controller
    {
        // GET: RideRequestsController
        public ActionResult Index()
        {
            return View();
        }

        // GET: RideRequestsController/Details/5
        public ActionResult Details(int id)
        {
            return View();
        }

        // GET: RideRequestsController/Create
        public ActionResult Create()
        {
            return View();
        }

        // POST: RideRequestsController/Create
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

        // GET: RideRequestsController/Edit/5
        public ActionResult Edit(int id)
        {
            return View();
        }

        // POST: RideRequestsController/Edit/5
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

        // GET: RideRequestsController/Delete/5
        public ActionResult Delete(int id)
        {
            return View();
        }

        // POST: RideRequestsController/Delete/5
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
