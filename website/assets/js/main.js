/* PCL SE — interaction layer (Guizang Swiss rebuild) */
(() => {
  "use strict";

  const header = document.getElementById("site-header");
  const nav = document.getElementById("site-nav");
  const menuBtn = document.getElementById("menu-btn");
  const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  /* Sticky header state */
  if (header) {
    const setStuck = () => header.classList.toggle("is-stuck", window.scrollY > 6);
    setStuck();
    window.addEventListener("scroll", setStuck, { passive: true });
  }

  /* Mobile menu */
  if (nav && menuBtn) {
    const navLinks = Array.from(nav.querySelectorAll("a"));

    const closeMenu = () => {
      nav.classList.remove("is-open");
      menuBtn.setAttribute("aria-expanded", "false");
      menuBtn.setAttribute("aria-label", "打开导航菜单");
      document.body.classList.remove("menu-open");
    };

    const openMenu = () => {
      nav.classList.add("is-open");
      menuBtn.setAttribute("aria-expanded", "true");
      menuBtn.setAttribute("aria-label", "关闭导航菜单");
      document.body.classList.add("menu-open");
    };

    menuBtn.addEventListener("click", () => {
      if (nav.classList.contains("is-open")) closeMenu();
      else openMenu();
    });

    navLinks.forEach((link) => link.addEventListener("click", closeMenu));

    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape" && nav.classList.contains("is-open")) {
        closeMenu();
        menuBtn.focus();
      }
    });

    window.addEventListener("resize", () => {
      if (window.innerWidth > 900) closeMenu();
    });
  }

  /* Scroll spy for the primary nav */
  const spyLinks = Array.from(document.querySelectorAll(".site-nav a[href^='#']"));
  const spySections = spyLinks
    .map((link) => document.querySelector(link.getAttribute("href")))
    .filter(Boolean);

  if (spySections.length && "IntersectionObserver" in window) {
    const spy = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (!entry.isIntersecting) return;
          spyLinks.forEach((link) => {
            const active = link.getAttribute("href") === "#" + entry.target.id;
            if (active) link.setAttribute("aria-current", "true");
            else link.removeAttribute("aria-current");
          });
        });
      },
      { rootMargin: "-45% 0px -50% 0px", threshold: 0 }
    );
    spySections.forEach((section) => spy.observe(section));
  }

  /* Reveal on scroll */
  const reveals = Array.from(document.querySelectorAll(".reveal"));
  if (reveals.length) {
    if (reduceMotion || !("IntersectionObserver" in window)) {
      reveals.forEach((el) => el.classList.add("is-in"));
    } else {
      const io = new IntersectionObserver(
        (entries, observer) => {
          entries.forEach((entry) => {
            if (entry.isIntersecting) {
              entry.target.classList.add("is-in");
              observer.unobserve(entry.target);
            }
          });
        },
        { rootMargin: "0px 0px -8% 0px", threshold: 0.08 }
      );
      reveals.forEach((el) => io.observe(el));
    }
  }

  /* Copy build command */
  const copyBtn = document.querySelector(".copy-btn");
  const feedback = document.getElementById("copy-feedback");

  const fallbackCopy = (text) => {
    const area = document.createElement("textarea");
    area.value = text;
    area.setAttribute("readonly", "");
    area.style.position = "fixed";
    area.style.top = "-9999px";
    document.body.appendChild(area);
    area.select();
    let ok = false;
    try {
      ok = document.execCommand("copy");
    } catch (error) {
      ok = false;
    }
    document.body.removeChild(area);
    return ok;
  };

  if (copyBtn && feedback) {
    copyBtn.addEventListener("click", async () => {
      const target = document.getElementById(copyBtn.dataset.copyTarget);
      if (!target) return;
      const text = target.textContent.trim();
      let ok = false;
      try {
        await navigator.clipboard.writeText(text);
        ok = true;
      } catch (error) {
        ok = fallbackCopy(text);
      }

      const label = copyBtn.querySelector("span");
      const original = label ? label.textContent : "";
      if (ok) {
        feedback.textContent = "已复制构建命令到剪贴板";
        if (label) label.textContent = "已复制";
      } else {
        feedback.textContent = "复制失败，请手动选中命令后复制";
      }
      copyBtn.classList.toggle("is-copied", ok);
      window.clearTimeout(copyBtn._resetTimer);
      copyBtn._resetTimer = window.setTimeout(() => {
        if (label) label.textContent = original || "复制";
        copyBtn.classList.remove("is-copied");
        feedback.textContent = "";
      }, 2600);
    });
  }

  /* Footer year */
  const year = document.getElementById("year");
  if (year) year.textContent = String(new Date().getFullYear());
})();
