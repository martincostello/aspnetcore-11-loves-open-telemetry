package com.grotspizzeria.orders;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.content;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.forwardedUrl;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.test.web.servlet.MockMvc;

/** The API documentation is for anyone, whereas the API itself needs a token. */
@SpringBootTest(properties = "jwt.signing-key=unit-test-signing-key-0123456789-abcdef")
@AutoConfigureMockMvc
class ApiDocumentationTests {

    @Autowired
    private MockMvc mvc;

    @Test
    void servesTheDocumentationPageAtTheRootWithoutAToken() throws Exception {
        // The root forwards to the page (a real server follows that; MockMvc does not), which is served as HTML
        mvc.perform(get("/"))
                .andExpect(status().isOk())
                .andExpect(forwardedUrl("index.html"));

        mvc.perform(get("/index.html"))
                .andExpect(status().isOk())
                .andExpect(content().contentTypeCompatibleWith("text/html"))
                .andExpect(content().string(org.hamcrest.Matchers.containsString("<title>grots-orders API")))
                .andExpect(content().string(org.hamcrest.Matchers.containsString("data-spec-url=\"/openapi.json\"")));

        mvc.perform(get("/swagger-ui/theme.css")).andExpect(status().isOk());
        mvc.perform(get("/swagger-ui/init.js")).andExpect(status().isOk());
    }

    @Test
    void describesTheApiInAnOpenApiDocumentWithoutAToken() throws Exception {
        mvc.perform(get("/openapi.json"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.info.title").value("grots-orders"))
                .andExpect(jsonPath("$.paths['/orders']").exists())
                .andExpect(jsonPath("$.paths['/orders/{id}']").exists())
                .andExpect(jsonPath("$.paths['/healthz']").exists())
                .andExpect(jsonPath("$.components.securitySchemes.bearerAuth.scheme").value("bearer"))
                .andExpect(jsonPath("$.paths['/healthz'].get.security").isEmpty())
                .andExpect(jsonPath("$.paths['/healthz'].get.tags").value(org.hamcrest.Matchers.contains("Health")))
                .andExpect(jsonPath("$.paths['/orders'].get.tags").value(org.hamcrest.Matchers.contains("Orders")))
                .andExpect(jsonPath("$.paths['/orders'].post.tags").value(org.hamcrest.Matchers.contains("Orders")))
                .andExpect(jsonPath("$.paths['/orders/{id}'].get.tags").value(org.hamcrest.Matchers.contains("Orders")))
                .andExpect(jsonPath("$.paths['/healthz'].get.responses['401']").doesNotExist());
    }

    @Test
    void stillNeedsATokenForTheApiItself() throws Exception {
        mvc.perform(get("/orders/ord_1")).andExpect(status().isUnauthorized());
    }
}
