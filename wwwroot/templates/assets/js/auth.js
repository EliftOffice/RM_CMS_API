(function () {
    function checkAuth() {
       
        const isLoggedIn = sessionStorage.getItem("isLoggedIn");

        if (!isLoggedIn || isLoggedIn === "false") {
          //  window.location.href = "https://rmoffice.online/";
            window.location.href = "https://localhost:7104/";
        }
    }

    // Run automatically when page loads
    window.addEventListener("load", checkAuth);
})();