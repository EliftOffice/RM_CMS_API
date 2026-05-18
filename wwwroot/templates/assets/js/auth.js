(function () {

    function getRootPath() {
        const { protocol, host } = window.location;
        return `${protocol}//${host}/`;
    }

    function checkAuth() {       
        const isLoggedIn = sessionStorage.getItem("isLoggedIn");

        if (!isLoggedIn || isLoggedIn === "false") {

            const rootPath = getRootPath();

            window.location.href = rootPath;
        }
    }

    // Run automatically when page loads
    window.addEventListener("load", checkAuth);

})();