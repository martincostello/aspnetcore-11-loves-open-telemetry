(function () {
    const picker = document.getElementById('theme');
    picker.value = window.grotsTheme.get();
    picker.addEventListener('change', () => window.grotsTheme.set(picker.value));

    const target = document.getElementById('swagger-ui');

    window.ui = SwaggerUIBundle({
        url: target.dataset.specUrl,
        dom_id: '#swagger-ui',
        deepLinking: true,
        displayRequestDuration: true,
        persistAuthorization: true,
        presets: [SwaggerUIBundle.presets.apis],
        layout: 'BaseLayout',
        validatorUrl: null,
    });
})();
