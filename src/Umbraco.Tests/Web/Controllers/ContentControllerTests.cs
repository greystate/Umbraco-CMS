﻿using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Http;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Umbraco.Core.Composing;
using Umbraco.Core.Models;
using Umbraco.Core.Models.Entities;
using Umbraco.Core.Models.Membership;
using Umbraco.Core.PropertyEditors;
using Umbraco.Core.Services;
using Umbraco.Tests.TestHelpers;
using Umbraco.Tests.TestHelpers.ControllerTesting;
using Umbraco.Tests.TestHelpers.Entities;
using Umbraco.Tests.Testing;
using Umbraco.Web;
using Umbraco.Web.Editors;
using Umbraco.Web.Models.ContentEditing;
using Umbraco.Web.PublishedCache;

using Task = System.Threading.Tasks.Task;
using Umbraco.Core.Dictionary;
using Umbraco.Web.PropertyEditors;
using System;
using Umbraco.Web.WebApi;
using Umbraco.Web.Trees;
using System.Globalization;
using Umbraco.Web.Actions;

namespace Umbraco.Tests.Web.Controllers
{
    [TestFixture]
    [UmbracoTest(Database = UmbracoTestOptions.Database.None)]
    public class ContentControllerTests : TestWithDatabaseBase
    {
        protected override void ComposeApplication(bool withApplication)
        {
            base.ComposeApplication(withApplication);

            //Replace with mockable services:

            var userServiceMock = new Mock<IUserService>();
            userServiceMock.Setup(service => service.GetUserById(It.IsAny<int>()))
                .Returns((int id) => id == 1234 ? new User(1234, "Test", "test@test.com", "test@test.com", "", new List<IReadOnlyUserGroup>(), new int[0], new int[0]) : null);
            userServiceMock.Setup(x => x.GetProfileById(It.IsAny<int>()))
                .Returns((int id) => id == 1234 ? new User(1234, "Test", "test@test.com", "test@test.com", "", new List<IReadOnlyUserGroup>(), new int[0], new int[0]) : null);
            userServiceMock.Setup(service => service.GetPermissionsForPath(It.IsAny<IUser>(), It.IsAny<string>()))
                .Returns(new EntityPermissionSet(123, new EntityPermissionCollection(new[]
                {
                    new EntityPermission(0, 123, new[]
                    {
                        ActionBrowse.ActionLetter.ToString(),
                        ActionUpdate.ActionLetter.ToString(),
                        ActionPublish.ActionLetter.ToString(),
                        ActionNew.ActionLetter.ToString()
                    }),
                })));

            var entityService = new Mock<IEntityService>();
            entityService.Setup(x => x.GetAllPaths(UmbracoObjectTypes.Document, It.IsAny<int[]>()))
                .Returns((UmbracoObjectTypes objType, int[] ids) => ids.Select(x => new TreeEntityPath { Path = $"-1,{x}", Id = x }).ToList());

            var dataTypeService = new Mock<IDataTypeService>();
            dataTypeService.Setup(service => service.GetDataType(It.IsAny<int>()))
                .Returns(Mock.Of<IDataType>(type => type.Id == 9876 && type.Name == "text"));
            dataTypeService.Setup(service => service.GetDataType(-87))  //the RTE
                .Returns(Mock.Of<IDataType>(type => type.Id == -87 && type.Name == "Rich text" && type.Configuration == new RichTextConfiguration()));

            var langService = new Mock<ILocalizationService>();
            langService.Setup(x => x.GetAllLanguages()).Returns(new[] {
                    Mock.Of<ILanguage>(x => x.IsoCode == "en-US"),
                    Mock.Of<ILanguage>(x => x.IsoCode == "es-ES"),
                    Mock.Of<ILanguage>(x => x.IsoCode == "fr-FR")
                });

            var textService = new Mock<ILocalizedTextService>();
            textService.Setup(x => x.Localize(It.IsAny<string>(), It.IsAny<CultureInfo>(), It.IsAny<IDictionary<string, string>>())).Returns("text");

            Container.RegisterSingleton(f => Mock.Of<IContentService>());
            Container.RegisterSingleton(f => Mock.Of<IContentTypeService>());
            Container.RegisterSingleton(f => userServiceMock.Object);
            Container.RegisterSingleton(f => entityService.Object);
            Container.RegisterSingleton(f => dataTypeService.Object);
            Container.RegisterSingleton(f => langService.Object);
            Container.RegisterSingleton(f => textService.Object);
            Container.RegisterSingleton(f => Mock.Of<ICultureDictionaryFactory>());
            Container.RegisterSingleton(f => new UmbracoApiControllerTypeCollection(new[] { typeof(ContentTreeController) }));
        }

        private MultipartFormDataContent GetMultiPartRequestContent(string json)
        {
            var multiPartBoundary = "----WebKitFormBoundary123456789";
            return new MultipartFormDataContent(multiPartBoundary)
            {
                new StringContent(json)
                {
                    Headers =
                    {
                        ContentDisposition = new ContentDispositionHeaderValue("form-data")
                        {
                            Name = "contentItem"
                        }
                    }
                }
            };
        }

        private IContent GetMockedContent()
        {
            var content = MockedContent.CreateSimpleContent(MockedContentTypes.CreateSimpleContentType());
            content.Id = 123;
            content.Path = "-1,123";
            //ensure things have ids
            var ids = 888;
            foreach (var g in content.PropertyGroups)
            {
                g.Id = ids;
                ids++;
            }
            foreach (var p in content.PropertyTypes)
            {
                p.Id = ids;
                ids++;
            }
            return content;
        }

        private const string PublishJsonInvariant = @"{
    ""id"": 123,
    ""contentTypeAlias"": ""page"",
    ""parentId"": -1,
    ""action"": ""save"",
    ""variants"": [
        {
            ""name"": ""asdf"",
            ""properties"": [
                {
                    ""id"": 1,
                    ""alias"": ""title"",
                    ""value"": ""asdf""
                }
            ],
            ""culture"": null,
            ""save"": true,
            ""publish"": true
        }
    ]
}";

        private const string PublishJsonVariant = @"{
    ""id"": 123,
    ""contentTypeAlias"": ""page"",
    ""parentId"": -1,
    ""action"": ""save"",
    ""variants"": [
        {
            ""name"": ""asdf"",
            ""properties"": [
                {
                    ""id"": 1,
                    ""alias"": ""title"",
                    ""value"": ""asdf""
                }
            ],
            ""culture"": ""en-US"",
            ""save"": true,
            ""publish"": true
        },
        {
            ""name"": ""asdf"",
            ""properties"": [
                {
                    ""id"": 1,
                    ""alias"": ""title"",
                    ""value"": ""asdf""
                }
            ],
            ""culture"": ""fr-FR"",
            ""save"": true,
            ""publish"": true
        },
        {
            ""name"": ""asdf"",
            ""properties"": [
                {
                    ""id"": 1,
                    ""alias"": ""title"",
                    ""value"": ""asdf""
                }
            ],
            ""culture"": ""es-ES"",
            ""save"": true,
            ""publish"": true
        }
    ]
}";

        /// <summary>
        /// Returns 404 if the content wasn't found based on the ID specified
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task PostSave_Validate_Existing_Content()
        {
            ApiController Factory(HttpRequestMessage message, UmbracoHelper helper)
            {
                var contentServiceMock = Mock.Get(Current.Services.ContentService);
                contentServiceMock.Setup(x => x.GetById(123)).Returns(() => null); //do not find it

                var propertyEditorCollection = new PropertyEditorCollection(new DataEditorCollection(Enumerable.Empty<DataEditor>()));
                var usersController = new ContentController(propertyEditorCollection);
                Container.InjectProperties(usersController);
                return usersController;
            }

            var runner = new TestRunner(Factory);
            var response = await runner.Execute("Content", "PostSave", HttpMethod.Post,
                content: GetMultiPartRequestContent(PublishJsonInvariant),
                mediaTypeHeader: new MediaTypeWithQualityHeaderValue("multipart/form-data"),
                assertOkResponse: false);

            Assert.AreEqual(HttpStatusCode.NotFound, response.Item1.StatusCode);
            Assert.AreEqual(")]}',\n{\"Message\":\"content was not found\"}", response.Item1.Content.ReadAsStringAsync().Result);

            //var obj = JsonConvert.DeserializeObject<PagedResult<UserDisplay>>(response.Item2);
            //Assert.AreEqual(0, obj.TotalItems);
        }

        [Test]
        public async Task PostSave_Validate_At_Least_One_Variant_Flagged_For_Saving()
        {
            ApiController Factory(HttpRequestMessage message, UmbracoHelper helper)
            {
                var contentServiceMock = Mock.Get(Current.Services.ContentService);
                contentServiceMock.Setup(x => x.GetById(123)).Returns(() => GetMockedContent());

                var propertyEditorCollection = new PropertyEditorCollection(new DataEditorCollection(Enumerable.Empty<DataEditor>()));
                var usersController = new ContentController(propertyEditorCollection);
                Container.InjectProperties(usersController);
                return usersController;
            }

            var json = JsonConvert.DeserializeObject<JObject>(PublishJsonInvariant);
            //remove all save flaggs
            ((JArray)json["variants"])[0]["save"] = false;

            var runner = new TestRunner(Factory);
            var response = await runner.Execute("Content", "PostSave", HttpMethod.Post,
                content: GetMultiPartRequestContent(JsonConvert.SerializeObject(json)),
                mediaTypeHeader: new MediaTypeWithQualityHeaderValue("multipart/form-data"),
                assertOkResponse: false);

            Assert.AreEqual(HttpStatusCode.NotFound, response.Item1.StatusCode);
            Assert.AreEqual(")]}',\n{\"Message\":\"No variants flagged for saving\"}", response.Item1.Content.ReadAsStringAsync().Result);
        }

        /// <summary>
        /// Returns 404 if any of the posted properties dont actually exist
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task PostSave_Validate_Properties_Exist()
        {
            ApiController Factory(HttpRequestMessage message, UmbracoHelper helper)
            {
                var contentServiceMock = Mock.Get(Current.Services.ContentService);
                contentServiceMock.Setup(x => x.GetById(123)).Returns(() => GetMockedContent());

                var propertyEditorCollection = new PropertyEditorCollection(new DataEditorCollection(Enumerable.Empty<DataEditor>()));
                var usersController = new ContentController(propertyEditorCollection);
                Container.InjectProperties(usersController);
                return usersController;
            }

            var json = JsonConvert.DeserializeObject<JObject>(PublishJsonInvariant);
            //add a non-existent property to a variant being saved
            var variantProps = (JArray)json["variants"].ElementAt(0)["properties"];
            variantProps.Add(JObject.FromObject(new
            {
                id = 2,
                alias = "doesntExist",
                value = "hello"
            }));

            var runner = new TestRunner(Factory);
            var response = await runner.Execute("Content", "PostSave", HttpMethod.Post,
                content: GetMultiPartRequestContent(JsonConvert.SerializeObject(json)),
                mediaTypeHeader: new MediaTypeWithQualityHeaderValue("multipart/form-data"),
                assertOkResponse: false);

            Assert.AreEqual(HttpStatusCode.NotFound, response.Item1.StatusCode);
        }

        [Test]
        public async Task PostSave_Simple_Invariant()
        {
            var content = GetMockedContent();

            ApiController Factory(HttpRequestMessage message, UmbracoHelper helper)
            {

                var contentServiceMock = Mock.Get(Current.Services.ContentService);
                contentServiceMock.Setup(x => x.GetById(123)).Returns(() => content);
                contentServiceMock.Setup(x => x.Save(It.IsAny<IContent>(), It.IsAny<int>(), It.IsAny<bool>()))
                    .Returns(new OperationResult(OperationResultType.Success, new Core.Events.EventMessages())); //success

                var contentTypeServiceMock = Mock.Get(Current.Services.ContentTypeService);
                contentTypeServiceMock.Setup(x => x.Get(content.ContentTypeId)).Returns(() => MockedContentTypes.CreateSimpleContentType());

                var propertyEditorCollection = new PropertyEditorCollection(new DataEditorCollection(Enumerable.Empty<DataEditor>()));
                var usersController = new ContentController(propertyEditorCollection);
                Container.InjectProperties(usersController);
                return usersController;
            }

            var runner = new TestRunner(Factory);
            var response = await runner.Execute("Content", "PostSave", HttpMethod.Post,
                content: GetMultiPartRequestContent(PublishJsonInvariant),
                mediaTypeHeader: new MediaTypeWithQualityHeaderValue("multipart/form-data"),
                assertOkResponse: false);

            Assert.AreEqual(HttpStatusCode.OK, response.Item1.StatusCode);
            var display = JsonConvert.DeserializeObject<ContentItemDisplay>(response.Item2);
            Assert.AreEqual(1, display.Variants.Count());
            Assert.AreEqual(content.PropertyGroups.Count(), display.Variants.ElementAt(0).Tabs.Count());
            Assert.AreEqual(content.PropertyTypes.Count(), display.Variants.ElementAt(0).Tabs.ElementAt(0).Properties.Count());
        }

        [Test]
        public async Task PostSave_Validate_Empty_Name()
        {
            var content = GetMockedContent();

            ApiController Factory(HttpRequestMessage message, UmbracoHelper helper)
            {

                var contentServiceMock = Mock.Get(Current.Services.ContentService);
                contentServiceMock.Setup(x => x.GetById(123)).Returns(() => content);
                contentServiceMock.Setup(x => x.Save(It.IsAny<IContent>(), It.IsAny<int>(), It.IsAny<bool>()))
                    .Returns(new OperationResult(OperationResultType.Success, new Core.Events.EventMessages())); //success

                var contentTypeServiceMock = Mock.Get(Current.Services.ContentTypeService);
                contentTypeServiceMock.Setup(x => x.Get(content.ContentTypeId)).Returns(() => MockedContentTypes.CreateSimpleContentType());

                var propertyEditorCollection = new PropertyEditorCollection(new DataEditorCollection(Enumerable.Empty<DataEditor>()));
                var usersController = new ContentController(propertyEditorCollection);
                Container.InjectProperties(usersController);
                return usersController;
            }

            //clear out the name
            var json = JsonConvert.DeserializeObject<JObject>(PublishJsonInvariant);
            json["variants"].ElementAt(0)["name"] = null;

            var runner = new TestRunner(Factory);
            var response = await runner.Execute("Content", "PostSave", HttpMethod.Post,
                content: GetMultiPartRequestContent(JsonConvert.SerializeObject(json)),
                mediaTypeHeader: new MediaTypeWithQualityHeaderValue("multipart/form-data"),
                assertOkResponse: false);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.Item1.StatusCode);
            var display = JsonConvert.DeserializeObject<ContentItemDisplay>(response.Item2);
            Assert.AreEqual(1, display.Errors.Count());
            Assert.IsTrue(display.Errors.ContainsKey("Variants[0].Name"));
            //ModelState":{"Variants[0].Name":["Required"]}
        }

        [Test]
        public async Task PostSave_Validate_Variants_Empty_Name()
        {
            var content = GetMockedContent();

            ApiController Factory(HttpRequestMessage message, UmbracoHelper helper)
            {

                var contentServiceMock = Mock.Get(Current.Services.ContentService);
                contentServiceMock.Setup(x => x.GetById(123)).Returns(() => content);
                contentServiceMock.Setup(x => x.Save(It.IsAny<IContent>(), It.IsAny<int>(), It.IsAny<bool>()))
                    .Returns(new OperationResult(OperationResultType.Success, new Core.Events.EventMessages())); //success

                var contentTypeServiceMock = Mock.Get(Current.Services.ContentTypeService);
                contentTypeServiceMock.Setup(x => x.Get(content.ContentTypeId)).Returns(() => MockedContentTypes.CreateSimpleContentType());

                var propertyEditorCollection = new PropertyEditorCollection(new DataEditorCollection(Enumerable.Empty<DataEditor>()));
                var usersController = new ContentController(propertyEditorCollection);
                Container.InjectProperties(usersController);
                return usersController;
            }

            //clear out one of the names
            var json = JsonConvert.DeserializeObject<JObject>(PublishJsonVariant);
            json["variants"].ElementAt(0)["name"] = null;

            var runner = new TestRunner(Factory);
            var response = await runner.Execute("Content", "PostSave", HttpMethod.Post,
                content: GetMultiPartRequestContent(JsonConvert.SerializeObject(json)),
                mediaTypeHeader: new MediaTypeWithQualityHeaderValue("multipart/form-data"),
                assertOkResponse: false);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.Item1.StatusCode);
            var display = JsonConvert.DeserializeObject<ContentItemDisplay>(response.Item2);
            Assert.AreEqual(2, display.Errors.Count());
            Assert.IsTrue(display.Errors.ContainsKey("Variants[0].Name"));
            Assert.IsTrue(display.Errors.ContainsKey("_content_variant_en-US_"));
        }

        //TODO: There are SOOOOO many more tests we should write - a lot of them to do with validation

    }
}
